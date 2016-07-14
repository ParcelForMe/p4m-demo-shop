using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Collections.Generic;
using System.Net.Http;
using OpenOrderFramework.Models;
using System.Linq;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using System.Net.Http.Headers;
using System.Net.Http.Formatting;
using Newtonsoft.Json;

namespace OpenOrderFramework.Controllers
{
    public class P4MController : Controller
    {
        ApplicationDbContext storeDB = new ApplicationDbContext();
        private ApplicationUserManager _userManager;
        public ApplicationUserManager UserManager
        {
            get
            {
                return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>();
            }
            private set
            {
                _userManager = value;
            }
        }
        const decimal _taxPercent = 0.20M;

        public P4MController()
        {
        }

        [HttpGet]
        [Route("checkout/p4mCheckout")]
        public async Task<ActionResult> P4MCheckout()
        {
            var localCart = ShoppingCart.GetCart(this.HttpContext);
            var cart = GetP4MCartFromLocalCart();
            if (localCart == null || cart == null || cart.Items.Count == 0)
                return Redirect("/home");
            // update P4M with the current cart details
            //await AddItemsToP4MCartAsync();
            // Return the view
            return View("P4MCheckout");
        }
        
        async Task AddItemsToP4MCartAsync()
        {
            // get the consumer's details from P4M. 
            var client = new HttpClient();
            var token = Request.Cookies["p4mToken"].Value;
            client.SetBearerToken(token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var cart = GetP4MCartFromLocalCart();
            var cartMessage = new PostCartMessage { Cart = cart, ClearItems = true, Currency = "GBP", PaymentType = "DB", SessionId = cart.SessionId };
            var content = new System.Net.Http.ObjectContent<PostCartMessage>(cartMessage, new JsonMediaTypeFormatter());
            var result = await client.PostAsync(P4MConstants.BaseApiAddress + "cart", content);
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<PostCartMessage>(messageString);
            if (!message.Success) {
                throw new Exception(message.Error);
            }             
        }

        P4MCart GetP4MCartFromLocalCart()
        {
            var localCart = ShoppingCart.GetCart(this.HttpContext);
            this.Response.Cookies[ShoppingCart.CartSessionKey].Value = localCart.ShoppingCartId;
            var p4mCart = new P4MCart
            {
                Reference = localCart.ShoppingCartId,
                SessionId = localCart.ShoppingCartId,
                Date = DateTime.UtcNow,
                Currency = "GBP",
                ShippingAmt = (double)localCart.Shipping,
                Tax = (double)localCart.Tax,
                Items = new List<P4MCartItem>()
            };

            foreach (var cartItem in localCart.Items)
            {
                var item = storeDB.Items.Single(i => i.ID == cartItem.ItemId);
                p4mCart.Items.Add(new P4MCartItem
                {
                    Make = item.Name,
                    Sku = item.ID.ToString(),
                    Desc = item.Name,
                    Qty = cartItem.Count,
                    Price = (double)item.Price,
                    LinkToImage = item.ItemPictureUrl,
                });
            }
            return p4mCart;
        }

        [HttpGet]
        [Route("getP4MCart")]
        public JsonResult GetCartWithItems()
        {
            var result = new CartMessage();
            try
            {
                result.Cart = GetP4MCartFromLocalCart();
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("shippingSelector")]
        public async Task<ActionResult> ShippingSelector()
        {
            // Return the view
            return View("P4MDelivery");
        }

        [HttpGet]
        [Route("applyDiscountCode")]
        public JsonResult ApplyDiscountCode(string discountCode)
        {
            var result = new DiscountMessage();
            try
            {
                var discount = storeDB.Discounts.SingleOrDefault(d => d.Code == discountCode);
                if (discount == null)
                    result.Error = string.Format("Discount code {0} does not exist", discountCode);
                else
                {
                    result.Description = discount.Description;
                    var localCart = ShoppingCart.GetCart(this.HttpContext);
                    localCart.CalcTax(discount);
                    result.Amount = localCart.Discount;
                    result.Tax = localCart.Tax;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Route("itemQtyChanged")]
        public async Task<JsonResult> ItemQtyChanged(List<ChangedItem> items)
        {
            var result = new CartUpdateMessage();
            try
            {
                var localCart = ShoppingCart.GetCart(this.HttpContext);
                foreach (var chgItem in items)
                {
                    var intCode = Convert.ToInt32(chgItem.ItemCode);
                    var item = storeDB.Items.Single(i => i.ID == intCode);
                    var roundQty = (int)Math.Round(chgItem.Qty);
                    await localCart.SetItemQtyAsync(item.ID, roundQty);
                }
                localCart.CalcTax();
                result.Tax = localCart.Tax;
                result.Shipping = localCart.Shipping;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("purchase/{cartId}/{cvv}")]
        public async Task<JsonResult> Purchase(string cartId, string cvv)
        {
            var result = new P4MBaseMessage();
            try
            {
                var token = Request.Cookies["p4mToken"].Value;
                var client = new HttpClient();
                client.SetBearerToken(token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var purchaseResult = await client.GetAsync(string.Format("{0}purchase/{1}/{2}",P4MConstants.BaseApiAddress, cartId, cvv));
                purchaseResult.EnsureSuccessStatusCode();
                var messageString = await purchaseResult.Content.ReadAsStringAsync();
                var message = JsonConvert.DeserializeObject<PurchaseMessage>(messageString);
                if (!message.Success)
                    throw new Exception(message.Error);
                ShoppingCart.GetCart(this).EmptyCart();
                HttpContext.Session[ShoppingCart.CartSessionKey] = null;
#pragma warning disable 4014
                // we've waited enough - don't wait for the order to be saved as well!
                CreateLocalOrderAsync(message);
#pragma warning restore 4014
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public async Task CreateLocalOrderAsync(PurchaseMessage purchase)
        {
            Order order = null;
            // get the current user details for creating the order
            var localId = User.Identity.GetUserId();
            var user = await UserManager.FindByIdAsync(localId);
            if (user == null)
                throw new Exception("No logged in user");
            // create and save the order, then store the id in a cookie
            order = new Order()
            {
                Username = User.Identity.Name,
                Email = User.Identity.Name,
                OrderDate = (DateTime)purchase.Cart.Date,
                Experation = DateTime.Now.AddYears(10),
                Address = purchase.DeliverTo.Street1,
                City = purchase.DeliverTo.City,
                PostalCode = purchase.DeliverTo.PostCode,
                State = purchase.DeliverTo.State,
                Country = purchase.DeliverTo.Country,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.Phone
            };
            //Save Order
            storeDB.Orders.Add(order);
            // get the new order Id
            storeDB.SaveChanges();
            // add the items
            foreach (var item in purchase.Cart.Items)
            {
                var ordItem = new OrderDetail {
                    ItemId = Convert.ToInt32(item.Sku),
                    OrderId = order.OrderId,
                    Quantity = (int)item.Qty,
                    UnitPrice = (decimal)item.Price
                };
                storeDB.OrderDetails.Add(ordItem);
            }
            storeDB.SaveChanges();
        }
    }
}