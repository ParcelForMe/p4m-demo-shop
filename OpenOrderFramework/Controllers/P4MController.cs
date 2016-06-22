using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Thinktecture.IdentityModel.Client;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens;
using System.Net.Http;
using OpenOrderFramework.Models;
using System.Linq;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
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
        public async Task<ActionResult> P4MCheckout(string session)
        {
            await GetOrderAsync();
            // Return the view
            return View("P4MCheckout");
        }

        // We're using HttpContextBase to allow access to cookies.
        public async Task<Order> GetOrderAsync()
        {
            Order order = null;
            if (this.HttpContext.Session[ShoppingCart.OrderSessionKey] == null)
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
                this.HttpContext.Session[ShoppingCart.OrderSessionKey] = order.OrderId.ToString();
            }
            else
            {
                var id = Convert.ToInt32(this.HttpContext.Session[ShoppingCart.OrderSessionKey]);
                order = storeDB.Orders.SingleOrDefault(o => o.OrderId == id);
                order.OrderDetails = storeDB.OrderDetails.Where(od => od.OrderId == id).ToList();
            }
            await AddItemsToP4MCartAsync(Request.Cookies["p4mToken"].Value, order.OrderId.ToString(), order);
            return order;
        }

        async Task AddItemsToP4MCartAsync(string token, string session, Order order)
        {
            // get the consumer's details from P4M. 
            var client = new HttpClient();
            client.SetBearerToken(token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var cart = new P4MCart {
                Reference = session,
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
            var cartMessage = new PostCartMessage { Cart = cart, ClearItems = true, Currency = "GBP", PaymentType = "DB", SessionId = session };
            var content = new System.Net.Http.ObjectContent<PostCartMessage>(cartMessage, new JsonMediaTypeFormatter());
            var result = await client.PostAsync(P4MConstants.BaseApiAddress + "cart", content);
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<PostCartMessage>(messageString);
            if (!message.Success)
                throw new Exception(message.Error);
        }

        [HttpGet]
        [Route("shippingSelector")]
        public async Task<ActionResult> ShippingSelector(string session)
        {
            var order = await GetOrderAsync();
            // Return the view
            return View("Delivery");
        }

        [HttpGet]
        [Route("applyDiscountCode")]
        public async Task<DiscountMessage> ApplyDiscountCodeAsync(string session, string discCode)
        {
            var result = new DiscountMessage();
            try
            {
                var discount = storeDB.Discounts.SingleOrDefault(d => d.Code == discCode);
                if (discount == null)
                    result.Error = string.Format("Discount code {0} does not exist", discCode);
                else
                {
                    var order = await GetOrderAsync();
                    result.Description = discount.Description;
                    CalcTax(order, discount);
                    storeDB.SaveChanges();
                    result.Amount = order.Discount;
                    result.Tax = order.Tax;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        void CalcTax(Order order, Discount discount = null)
        {
            var itemsTotal = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);
            if (discount != null)
                order.Discount = (itemsTotal + order.Shipping) * (discount.Percentage / 100);
            order.Tax = (itemsTotal + order.Shipping - order.Discount) * _taxPercent;
            order.Total = itemsTotal + order.Shipping + order.Tax - order.Discount;
        }

        [HttpGet]
        [Route("itemQtyChanged")]
        public async Task<CartUpdateMessage> ItemQtyChanged(string session, string itemCode, decimal qty)
        {
            var result = new CartUpdateMessage();
            try
            {
                var order = await GetOrderAsync();
                var intCode = Convert.ToInt32(itemCode);
                var item = storeDB.Items.Single(i => i.ID == intCode);
                var roundQty = (int)Math.Round(qty);
                if (roundQty <= 0)
                    RemoveFromCart(order, item.ID);
                else
                    UpdItem(order, item, roundQty);
                CalcTax(order);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        void UpdItem(Order order, Item item, int qty)
        {
            // Get the matching cart and item instances
            var orderItem = storeDB.OrderDetails.SingleOrDefault(c => c.OrderId == order.OrderId && c.ItemId == item.ID);

            if (orderItem == null)
            {
                // Create a new cart item if no cart item exists
                var orderDetail = new OrderDetail
                {
                    ItemId = item.ID,
                    OrderId = order.OrderId,
                    UnitPrice = item.Price,
                    Quantity = qty
                };
                orderItem = storeDB.OrderDetails.Add(orderDetail);
            }
            else
            {
                orderItem.Quantity = qty;
            }
            // Save changes
            storeDB.SaveChanges();
        }

        void RemoveFromCart(Order order, int itemId)
        {
            var orderItem = storeDB.OrderDetails.SingleOrDefault(c => c.OrderId == order.OrderId && c.ItemId == itemId);
            if (orderItem != null)
            {
                storeDB.OrderDetails.Remove(orderItem);
                // Save changes
                storeDB.SaveChanges();
            }
        }
    }
}