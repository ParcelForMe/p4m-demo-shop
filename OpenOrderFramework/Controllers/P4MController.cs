﻿using System;
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
using System.Text;
using Thinktecture.IdentityModel.Client;

namespace OpenOrderFramework.Controllers
{
    public class P4MController : Controller
    {
        ApplicationDbContext storeDB = new ApplicationDbContext();
        private ApplicationUserManager _userManager;
        static HttpClient _httpClient = new HttpClient();
        static P4MConsts _p4mConsts = new P4MConsts();

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
        [Route("p4m/checkout")]
        public async Task<ActionResult> P4MCheckout()
        {
            if (Request.Cookies["p4mToken"] == null || string.IsNullOrWhiteSpace(Request.Cookies["p4mToken"].Value))
            {
                // no token so we must be in exclusive mode
                if (P4MConsts.CheckoutMode != CheckoutMode.Exclusive || !await GetGuestTokenAsync())
                    return Redirect("/home");
            }
            var localCart = ShoppingCart.GetCart(this.HttpContext);
            var cart = GetP4MCartFromLocalCart();
            if (localCart == null || cart == null || cart.Items.Count == 0)
                return Redirect("/home");

            //var token = Response.Cookies["gfsCheckoutToken"].Value;
            //if (string.IsNullOrWhiteSpace(token))
            //    token = await GetGFSTokenAsync();
            //ViewBag.AccessToken = token;
            ViewBag.HostType = _p4mConsts.AppMode;
            ViewBag.InitialCountryCode = P4MConsts.DefaultInitialCountryCode;
            ViewBag.InitialPostCode = P4MConsts.DefaultInitialPostCode;
            // Return the view
            return View("P4MCheckout");
        }

        async Task<TokenResponse> GetGFSTokenAsync()
        {
            var uri = new Uri(@"https://identity.justshoutgfs.com/connect/token");

            var client = new Thinktecture.IdentityModel.Client.OAuth2Client(
                uri, P4MConsts.GfsClientId, P4MConsts.GfsClientSecret);

            var tokenResponse = await client.RequestClientCredentialsAsync("read checkout-api");
            if (tokenResponse == null || tokenResponse.AccessToken == null)
            {
                throw new Exception("Request for client credentials denied");
            }
            //Response.Cookies["gfsCheckoutToken"].Value = token;
            //Response.Cookies["gfsCheckoutToken"].Expires = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            return tokenResponse;
        }

        [HttpGet]
        [Route("p4m/renewShippingToken")]
        public async Task<JsonResult> GetNewShippingToken()
        {
            var result = new TokenMessage();
            try
            {
                var resp = await GetGFSTokenAsync();
                result.Token = resp.AccessToken;
                result.Expires = DateTime.UtcNow.AddSeconds(resp.ExpiresIn).ToString("yyyy-MM-ddThh:mm:ssZ"); 
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        async Task<bool> GetGuestTokenAsync()
        {
            // consumer is unknown so if we're in exclusive mode we need a guest token to access the P4M API
            var clientToken = await P4MHelpers.GetClientTokenAsync();
            _httpClient.SetBearerToken(clientToken.AccessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var locale = Request.Cookies["p4mLocale"].Value;
            var result = await _httpClient.GetAsync($"{_p4mConsts.BaseApiAddress}guestAccessToken/{locale}");
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<TokenMessage>(messageString);
            if (message.Success)
            {
                Response.Cookies["p4mToken"].Value = message.Token;
                //Response.Cookies["p4mTokenType"].Value = "Guest";
            }
            return message.Success;
        }

        //async Task AddItemsToP4MCartAsync()
        //{
        //    // update P4M with the current cart details
        //    var client = new HttpClient();
        //    var token = Request.Cookies["p4mToken"].Value;
        //    client.SetBearerToken(token);
        //    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        //    var cart = GetP4MCartFromLocalCart();
        //    var cartMessage = new PostCartMessage { Cart = cart, ClearItems = true, Currency = "GBP", PaymentType = "DB", SessionId = cart.SessionId };
        //    var content = new System.Net.Http.ObjectContent<PostCartMessage>(cartMessage, new JsonMediaTypeFormatter());
        //    var result = await client.PostAsync(_urls.BaseApiAddress + "cart", content);
        //    var messageString = await result.Content.ReadAsStringAsync();
        //    var message = JsonConvert.DeserializeObject<PostCartMessage>(messageString);
        //    if (!message.Success) {
        //        throw new Exception(message.Error);
        //    }             
        //}

        P4MCart GetP4MCartFromLocalCart()
        {
            var localCart = ShoppingCart.GetCart(this.HttpContext);
            this.Response.Cookies[ShoppingCart.CartSessionKey].Value = localCart.ShoppingCartId;
            var p4mCart = new P4MCart
            {
                SessionId = P4MConsts.SessionId, //localCart.ShoppingCartId,
                Date = DateTime.UtcNow,
                PaymentType = "PA",
                Currency = "GBP",
                ShippingAmt = (double)localCart.Shipping,
                Tax = (double)localCart.Tax,
                Items = new List<P4MCartItem>()
            };

            foreach (var cartItem in localCart.Items)
            {
                var item = storeDB.Items.Single(i => i.ID == cartItem.ItemId);
                var picUrl = item.ItemPictureUrl;
                if (!picUrl.StartsWith("http") && !picUrl.StartsWith("/"))
                    picUrl = "http://localhost:3000/" + picUrl;
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
            if (localCart.Discounts != null && localCart.Discounts.Count > 0)
            {
                foreach(var disc in localCart.Discounts)
                    p4mCart.Discounts = new List<P4MDiscount> { new P4MDiscount { Code = disc.DiscountCode, Description = disc.Description, Amount = (double)disc.Amount } };
            }
            return p4mCart;
        }

        [HttpGet]
        [Route("p4m/getP4MCart")]
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

        [HttpPost]
        [Route("p4m/restoreLastCart")]
        public async Task<JsonResult> RestoreLastCart(P4MCart cart)
        {
            var result = new P4MBaseMessage();
            try
            {
                await CreateLocalCartFromP4MCart(cart);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        //[HttpGet]
        //[Route("p4m/restoreLastCart")]
        //public async Task<JsonResult> RestoreLastCart()
        //{
        //    var result = new P4MBaseMessage();
        //    try
        //    {
        //        var cart = await GetOpenCartFromP4M();
        //        var dateStr = cart.ExpDeliveryDate != null ? ((DateTime)cart.ExpDeliveryDate).ToString("yyyy-MM-ddThh:mm:ssZ") : string.Empty;
        //        Response.Cookies["p4mCart"].Value = cart.Id + "|" + cart.AddressId + "|" + cart.DropPointId + "|" + cart.ServiceId + "|" + dateStr;
        //        await CreateLocalCartFromP4MCart(cart);
        //    }
        //    catch (Exception e)
        //    {
        //        result.Error = e.Message;
        //    }
        //    P4MHelpers.RemoveCookie(Response, "p4mOfferCartRestore");
        //    return Json(result, JsonRequestBehavior.AllowGet);
        //}

        async Task<P4MCart> GetOpenCartFromP4M()
        {
            // retrieve the last unpurchased cart details
            var token = Request.Cookies["p4mToken"].Value;
            
            _httpClient.SetBearerToken(token);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var sessionId = HttpContext.Session[ShoppingCart.CartSessionKey].ToString();
            var result = await _httpClient.GetAsync(string.Format("{0}restoreLastCart/{1}", _p4mConsts.BaseApiAddress, sessionId));
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<CartMessage>(messageString);
            if (!message.Success)
                throw new Exception(message.Error);
            return message.Cart;
        }

        async Task CreateLocalCartFromP4MCart(P4MCart p4mCart)
        {
            var localCart = ShoppingCart.GetCart(HttpContext);
            localCart.Shipping = (decimal)p4mCart.ShippingAmt;
            localCart.Tax = (decimal)p4mCart.Tax;
            if (p4mCart.Discounts != null)
                foreach(var discount in p4mCart.Discounts)
                {
                    localCart.Discounts.Add(new CartDiscount
                    {
                        DiscountCode = discount.Code,
                        Description = discount.Description,
                        Amount = (decimal)discount.Amount
                    });
                }
            if (p4mCart.Items != null)
                foreach (var item in p4mCart.Items)
                {
                    var localItem = storeDB.Items.Find(Convert.ToInt32(item.Sku));
                    if (localItem != null)
                        localCart.AddToCart(localItem, (int)Math.Round(item.Qty));
                }
            await storeDB.SaveChangesAsync();
        }

        /*
        [HttpGet]
        [Route("p4m/shippingSelector")]
        public ActionResult ShippingSelector()
        {
            // get an access token for GFS checkout
            var token = Response.Cookies["gfsCheckoutToken"].Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                var uri = new Uri(@"https://identity.justshoutgfs.com/connect/token");
                var client = new Thinktecture.IdentityModel.Client.OAuth2Client(
                    uri,
                   "parcel_4_me",
                   "needmoreparcels");
                   // "ambitious_alice",
                   // "m@dhatt3r");

                var tokenResponse = client.RequestClientCredentialsAsync("read checkout-api").Result;
                token = Helpers.Base64Encode(tokenResponse.AccessToken);
                Response.Cookies["gfsCheckoutToken"].Value = token;
                Response.Cookies["gfsCheckoutToken"].Expires = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            }
            ViewBag.AccessToken = token;
            return View("P4MDelivery");
        }*/

        [HttpPost]
        [Route("p4m/updShippingService")]
        public JsonResult ShippingDetails(ShippingDetails details)
        {
            var result = new CartTotalsMessage();
            try
            {
                var localCart = ShoppingCart.GetCart(this.HttpContext);
                localCart.Shipping = details.Amount;
                GetCartTotals(result, localCart);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet); 
        }

        [HttpPost]
        [Route("p4m/applyDiscountCode")]
        public JsonResult ApplyDiscountCode(string discountCode)
        {
            var result = new DiscountMessage();
            try
            {
                discountCode = discountCode.ToUpper();
                var discount = storeDB.Discounts.SingleOrDefault(d => d.Code.ToUpper() == discountCode);
                if (discount == null)
                    result.Error = string.Format("Discount code {0} does not exist", discountCode);
                else
                {
                    result.Description = discount.Description;
                    var localCart = ShoppingCart.GetCart(this.HttpContext);
                    var disc = localCart.Discounts.Where(d => d.CartId == localCart.ShoppingCartId && d.DiscountCode == discount.Code).FirstOrDefault();
                    if (disc == null)
                    {
                        disc = new CartDiscount { DiscountCode = discount.Code, CartId = localCart.ShoppingCartId, Description = discount.Description };
                        storeDB.CartDiscounts.Add(disc);
                        storeDB.SaveChanges();
                    }
                    GetCartTotals(result, localCart);
                    result.Code = discountCode;
                    disc = localCart.Discounts.Where(d => d.CartId == localCart.ShoppingCartId && d.DiscountCode == discount.Code).FirstOrDefault();
                    result.Amount = disc.Amount;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Route("p4m/removeDiscountCode")]
        public JsonResult RemoveDiscountCode(string discountCode)
        {
            var result = new DiscountMessage();
            try
            {
                var localCart = ShoppingCart.GetCart(this.HttpContext);
                var disc = storeDB.CartDiscounts.Where(d => d.CartId == localCart.ShoppingCartId && d.DiscountCode == discountCode).FirstOrDefault();
                if (disc != null)
                {
                    storeDB.CartDiscounts.Remove(disc);
                    storeDB.SaveChanges();
                    GetCartTotals(result, localCart);
                    result.Code = discountCode;
                    result.Amount = localCart.Discount;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Route("p4m/itemQtyChanged")]
        public async Task<JsonResult> ItemQtyChanged(CartUpdates cartUpdates)
        {
            var result = new CartUpdateMessage();
            try
            {
                var localCart = ShoppingCart.GetCart(this.HttpContext);
                if (cartUpdates.ShippingDetails != null)
                    localCart.Shipping = cartUpdates.ShippingDetails.Amount;
                foreach (var chgItem in cartUpdates.Items)
                {
                    var intCode = Convert.ToInt32(chgItem.ItemCode);
                    var item = storeDB.Items.Single(i => i.ID == intCode);
                    var roundQty = (int)Math.Round(chgItem.Qty);
                    await localCart.SetItemQtyAsync(item.ID, roundQty);
                }
                GetCartTotals(result, localCart);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        void GetCartTotals(CartTotalsMessage result, ShoppingCart localCart = null)
        {
            if (localCart == null)
                localCart = ShoppingCart.GetCart(this.HttpContext);
            localCart.CalcTax();
            result.Tax = localCart.Tax;
            result.Shipping = localCart.Shipping;
            result.Discount = localCart.Discount;
            result.Total = localCart.Total;
        }

        [HttpPost]
        [Route("p4m/purchase")]
        public async Task<JsonResult> Purchase(string cartId, string cvv, decimal cartTotal, string carrier = null)
        {
            var result = new PurchaseResultMessage();
            try
            {
                if (string.IsNullOrWhiteSpace(cvv))
                    throw new Exception("Please enter your CVV");
                // validate that the cart total from the widget is correct to prevent cart tampering in the browser
                cartTotal = Math.Round(cartTotal, 2);
                var localCart = ShoppingCart.GetCart(HttpContext);
                localCart.CalcTax();
                //decimal cartTot = Convert.ToDecimal(cartTotal);
                if (cartTotal != localCart.Total)
                {
                    localCart.EmptyCart();
                    HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    throw new Exception("Your cart is invalid and has been cleared. We're sorry for any inconvenience. Please keep shopping");
                }

                var orderId = await CreateLocalOrderAsync(localCart);
                
                var token = Request.Cookies["p4mToken"].Value;
                _httpClient.SetBearerToken(token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var purchaseMessage = new PostPurchaseMessage { CartId = cartId, CVV = cvv, OrderId = orderId.ToString(), Carrier = carrier };
                var content = new ObjectContent<PostPurchaseMessage>(purchaseMessage, new JsonMediaTypeFormatter());
                var apiResult = await _httpClient.PostAsync(_p4mConsts.BaseApiAddress + "purchase", content);

//                var apiResult = await client.GetAsync(string.Format("{0}purchase/{1}/{2}", _urls.BaseApiAddress, cartId, cvv));
                apiResult.EnsureSuccessStatusCode();
                var messageString = await apiResult.Content.ReadAsStringAsync();
                var purchaseResult = JsonConvert.DeserializeObject<PurchaseResultMessage>(messageString);
                if (!purchaseResult.Success)
                {
                    if (purchaseResult.Error.Contains("has already been processed!"))
                    {
                        ShoppingCart.GetCart(this).EmptyCart();
                        HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    }
                    throw new Exception(purchaseResult.Error);
                }
                // if ACSUrl is blank then the purchase proceeded without 3D Secure
                if (purchaseResult.ACSUrl == null)
                {
                    // purchaseResult includes the transaction Id, auth code and cart 
                    // so the retailer can store whatever is required at this point
                    ShoppingCart.GetCart(this).EmptyCart();
                    HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    orderId = await CreateLocalOrderAsync(localCart);
                    result.RedirectUrl = this.Url.Action("Complete", "Checkout", new { id = orderId }, this.Request.Url.Scheme);
                }
                else
                {
                    // retailer has opted to use 3D Secure and the consumer is enrolled
                    result.ACSUrl = purchaseResult.ACSUrl;
                    result.PaReq = purchaseResult.PaReq;
                    result.ACSResponseUrl = purchaseResult.ACSResponseUrl;
                    result.P4MData = purchaseResult.P4MData;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("p4m/paypalSetup")]
        public async Task<JsonResult> PaypalSetup(string cartId, decimal cartTotal, string carrier = null)
        {
            // this is the first part of a paypal transaction, which sends a request from P4M to Realex
            // when this returns we redirect the consumer to PP in a popup window
            var result = new TokenMessage();
            try
            {
                // validate that the cart total from the widget is correct to prevent cart tampering in the browser
                cartTotal = Math.Round(cartTotal, 2);
                var localCart = ShoppingCart.GetCart(HttpContext);
                localCart.CalcTax();
                //decimal cartTot = Convert.ToDecimal(cartTotal);
                if (cartTotal != localCart.Total)
                {
                    localCart.EmptyCart();
                    HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    throw new Exception("Your cart is invalid and has been cleared. We're sorry for any inconvenience. Please keep shopping");
                }

                var orderId = await CreateLocalOrderAsync(localCart);

                var token = Request.Cookies["p4mToken"].Value;
                _httpClient.SetBearerToken(token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var purchaseMessage = new PostPurchaseMessage { CartId = cartId, OrderId = orderId.ToString(), Carrier = carrier };
                var content = new ObjectContent<PostPurchaseMessage>(purchaseMessage, new JsonMediaTypeFormatter());
                var apiResult = await _httpClient.PostAsync(_p4mConsts.BaseApiAddress + "paypalSetup", content);

                //var apiResult = await _httpClient.GetAsync(string.Format("{0}paypalSetup/{1}", _urls.BaseApiAddress, cartId));
                //apiResult.EnsureSuccessStatusCode();
                var messageString = await apiResult.Content.ReadAsStringAsync();
                var setupResult = JsonConvert.DeserializeObject<TokenMessage>(messageString);
                if (!setupResult.Success)
                {
                    if (setupResult.Error.Contains("has already been processed!"))
                    {
                        ShoppingCart.GetCart(this).EmptyCart();
                        HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    }
                    throw new Exception(setupResult.Error);
                }
                result.Token = setupResult.Token;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("p4m/paypalCancel")]
        public HttpResponseMessage PaypalCancel(string pasref, string token, string PayerID)
        {
            // just close the PP popup
            var htmlResponse = new HttpResponseMessage();
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><body>");
            sb.AppendLine("<script>window.close();</script>");
            sb.AppendLine("</body></html>");
            htmlResponse.Content = new StringContent(sb.ToString());
            htmlResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return htmlResponse;
        }

        public async Task<int> CreateLocalOrderAsync(ShoppingCart cart)
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
                OrderDate = DateTime.UtcNow,
                Experation = DateTime.Now.AddYears(10),
                Address = "123 Main St",
                City = "Gotham City",
                PostalCode = "ABC123",
                State = "Gotham State",
                Country = "GB",
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.Phone
            };
            //Save Order
            storeDB.Orders.Add(order);
            // get the new order Id
            storeDB.SaveChanges();
            // add the items
            foreach (var item in cart.Items)
            {
                var prod = await storeDB.Items.FindAsync(item.ItemId);
                var ordItem = new OrderDetail
                {
                    ItemId = item.ItemId,
                    OrderId = order.OrderId,
                    Quantity = item.Count,
                    UnitPrice = prod.Price
                };
                storeDB.OrderDetails.Add(ordItem);
            }
            storeDB.SaveChanges();
            return order.OrderId;
        }

        [HttpGet]
        [Route("p4m/purchaseComplete/{orderId}")]
        public ActionResult PurchaseComplete(string orderId)
        {
            ShoppingCart.GetCart(this).EmptyCart();
            HttpContext.Session[ShoppingCart.CartSessionKey] = null;
            return RedirectToAction("Complete", "Checkout", new { id = orderId });
        }
        //        public ActionResult ThreeDSPurchaseComplete(string purchaseResult)
        //        {
        //            // purchaseResult is a purchase message JSON string, base64 encoded
        //            var decoded = Base64Decode(purchaseResult);
        //            var result = JsonConvert.DeserializeObject<PurchaseMessage>(decoded);
        //            if (result.Success)
        //            {
        //                // purchaseResult includes the transaction Id, auth code and cart 
        //                // so the retailer can store whatever is required at this point
        //                ShoppingCart.GetCart(this).EmptyCart();
        //                HttpContext.Session[ShoppingCart.CartSessionKey] = null;
        //#pragma warning disable 4014
        //                // we've waited enough - don't wait for the order to be saved as well!
        //                CreateLocalOrderAsync(result);
        //#pragma warning restore 4014
        //            }
        //            return View("P4M3DSPurchaseComplete", result);
        //        }

        async Task<CartMessage> GetCartFromP4MAsync(string cartId)
        {
            // locally store the purchased cart from P4M
            var token = Request.Cookies["p4mToken"].Value;
            _httpClient.SetBearerToken(token);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var apiResult = await _httpClient.GetAsync(string.Format("{0}cart/{1}?wantAddresses=true", _p4mConsts.BaseApiAddress, cartId));
            var messageString = await apiResult.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<CartMessage>(messageString);
            if (!result.Success)
                throw new Exception(result.Error);
            return result;
        }
    }
}