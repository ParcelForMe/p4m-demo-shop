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
            await AddItemsToP4MCartAsync();
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
            if (!message.Success)
                throw new Exception(message.Error);
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
            return View("Delivery");
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

        public async Task<Order> CreateLocalOrderAsync()
        {
            Order order = null;
            if (this.Request.Cookies[ShoppingCart.OrderSessionKey] == null)
            {
                // get the current user details for creating the order
                var localId = User.Identity.GetUserId();
                var user = await UserManager.FindByIdAsync(localId);
                if (user == null)
                    throw new Exception("No logged in user");
                // create and save the order, then store the id in a cookie
                var cart = ShoppingCart.GetCart(this.HttpContext);
                order = new Order()
                {
                    Username = User.Identity.Name,
                    Email = User.Identity.Name,
                    OrderDate = DateTime.Now,
                    Experation = DateTime.Now.AddYears(10),
                    Address = user.Address,
                    City = user.City,
                    PostalCode = user.PostalCode,
                    State = user.State,
                    Country = user.Country,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Phone = user.Phone
                };
                //Save Order
                storeDB.Orders.Add(order);
                try
                {
                    // get the new order Id
                    await storeDB.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    throw e;
                }
                // add cart details
                order = cart.CreateOrder(order);
                // Send tempCartId back to client as a cookie
                this.Response.Cookies[ShoppingCart.OrderSessionKey].Value = order.OrderId.ToString();
                //this.Response.Cookies[ShoppingCart.OrderSessionKey].Expires = DateTime.UtcNow.AddYears(1); - we want this to expire when the browser closes
                //this.HttpContext.Session[ShoppingCart.OrderSessionKey] = order.OrderId.ToString();
            }
            else
            {
                var id = Convert.ToInt32(this.Request.Cookies[ShoppingCart.OrderSessionKey].Value);
                order = storeDB.Orders.SingleOrDefault(o => o.OrderId == id);
                order.OrderDetails = storeDB.OrderDetails.Where(od => od.OrderId == id).ToList();
            }
            return order;
        }

        [HttpGet]
        [Route("purchase/{cartId}/{cvv}")]
        public async Task<JsonResult> Purchase(string cartId, string cvv)
        {
            var result = new PaymentMessage();
            try
            {
                var token = this.Request.Cookies["p4mToken"].Value;
                var client = new HttpClient();
                client.SetBearerToken(token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var purchaseResult = await client.GetAsync(string.Format("{0}purchase/{1}/{2}",P4MConstants.BaseApiAddress, cartId, cvv));
                var messageString = await purchaseResult.Content.ReadAsStringAsync();
                var message = JsonConvert.DeserializeObject<PaymentMessage>(messageString);
                if (!message.Success)
                    throw new Exception(message.Error);
                result.AuthCode = message.AuthCode;
                result.Id = message.Id;
                result.TransactionTypeCode = message.TransactionTypeCode;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        P4MCart GetP4MCartFromOrder(string session, Order order)
        {
            var cart = new P4MCart
            {
                Reference = session,
                SessionId = session,
                Date = DateTime.UtcNow,
                Currency = "GBP",
                ShippingAmt = 10,
                Tax = 0,
                Items = new List<P4MCartItem>()
            };

            foreach (var ordItem in order.OrderDetails)
            {
                var item = storeDB.Items.Single(i => i.ID == ordItem.ItemId);
                cart.Items.Add(new P4MCartItem
                {
                    Make = item.Name,
                    Sku = item.ID.ToString(),
                    Desc = item.Name,
                    Qty = ordItem.Quantity,
                    Price = (double)item.Price,
                    LinkToImage = item.ItemPictureUrl,
                });
            }
            return cart;
        }

        void CalcTaxFromOrder(Order order, Discount discount = null)
        {
            var itemsTotal = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);
            if (discount != null)
                order.Discount = (itemsTotal + order.Shipping) * (discount.Percentage / 100);
            order.Tax = (itemsTotal + order.Shipping - order.Discount) * _taxPercent;
            order.Total = itemsTotal + order.Shipping + order.Tax - order.Discount;
        }
    }
}