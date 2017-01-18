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
using System.Text;

namespace OpenOrderFramework.Controllers
{
    public class P4MController : Controller
    {
        ApplicationDbContext storeDB = new ApplicationDbContext();
        private ApplicationUserManager _userManager;
        static HttpClient _httpClient = new HttpClient();
        static P4MUrls _urls = new P4MUrls();

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
                if (P4MUrls.CheckoutMode != CheckoutMode.Exclusive || !await GetGuestTokenAsync())
                    return Redirect("/home");
            }
            var localCart = ShoppingCart.GetCart(this.HttpContext);
            var cart = GetP4MCartFromLocalCart();
            if (localCart == null || cart == null || cart.Items.Count == 0)
                return Redirect("/home");

            var token = Response.Cookies["gfsCheckoutToken"].Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                var uri = new Uri(@"https://identity.justshoutgfs.com/connect/token");
                
                var client = new Thinktecture.IdentityModel.Client.OAuth2Client(
                    uri,
                   "parcel_4_me",
                   "needmoreparcels");

                   //"ambitious_alice",
                   //"m@dhatt3r");

                var tokenResponse = client.RequestClientCredentialsAsync("read checkout-api").Result;
                if (tokenResponse == null || tokenResponse.AccessToken == null)
                {
                    throw new Exception("Request for client credentials denied");
                }
                token = Base64Encode(tokenResponse.AccessToken);
                Response.Cookies["gfsCheckoutToken"].Value = token;
                Response.Cookies["gfsCheckoutToken"].Expires = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            }
            ViewBag.AccessToken = token;
            ViewBag.HostType = _urls.AppMode;
            /*ViewBag.InitialAddress = Request.Cookies["p4mInitialAddress"]?.Value;
            ViewBag.InitialPostCode = Request.Cookies["p4mDefaultPostCode"]?.Value;
            ViewBag.InitialCountryCode = Request.Cookies["p4mDefaultCountryCode"]?.Value;
            if (ViewBag.InitialCountryCode == null)
            {
                ViewBag.InitialCountryCode = P4MUrls.DefaultInitialCountryCode;
            }
            if (ViewBag.InitialPostCode == null)
            {
                ViewBag.InitialPostCode = P4MUrls.DefaultInitialPostCode;
            }*/

            //var gfsCheckoutInitialPostJson = GetGfsCheckoutPost();
            //ViewBag.InitialData = Base64Encode(gfsCheckoutInitialPostJson);
            
            // Return the view
            return View("P4MCheckout");
        }

        async Task<bool> GetGuestTokenAsync()
        {
            // consumer is unknown so if we're in exclusive mode we need a guest token to access the P4M API
            var clientToken = await P4MHelpers.GetClientTokenAsync();
            _httpClient.SetBearerToken(clientToken.AccessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var locale = Request.Cookies["p4mLocale"].Value;
            var result = await _httpClient.GetAsync($"{_urls.BaseApiAddress}guestAccessToken/{locale}");
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<TokenMessage>(messageString);
            if (message.Success)
            {
                Response.Cookies["p4mToken"].Value = message.Token;
                Response.Cookies["p4mTokenType"].Value = "Guest";
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
                Reference = localCart.ShoppingCartId,
                SessionId = localCart.ShoppingCartId,
                Date = DateTime.UtcNow,
                PaymentType = "DB",
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

        [HttpGet]
        [Route("p4m/restoreLastCart")]
        public async Task<JsonResult> RestoreLastCart()
        {
            var result = new P4MBaseMessage();
            try
            {
                var cart = await GetOpenCartFromP4M();
                await CreateLocalCartFromP4MCart(cart);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            P4MHelpers.RemoveCookie(Response, "p4mOfferCartRestore");
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        async Task<P4MCart> GetOpenCartFromP4M()
        {
            // retrieve the last unpurchased cart details
            var token = Request.Cookies["p4mToken"].Value;
            
            _httpClient.SetBearerToken(token);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var sessionId = HttpContext.Session[ShoppingCart.CartSessionKey].ToString();
            var result = await _httpClient.GetAsync(string.Format("{0}restoreLastCart/{1}", _urls.BaseApiAddress, sessionId));
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
            foreach(var discount in p4mCart.Discounts)
            {
                localCart.Discounts.Add(new CartDiscount
                {
                    DiscountCode = discount.Code,
                    Description = discount.Description,
                    Amount = (decimal)discount.Amount
                });
            }
            foreach (var item in p4mCart.Items)
            {
                var localItem = storeDB.Items.Find(Convert.ToInt32(item.Sku));
                localCart.AddToCart(localItem, (int)Math.Round(item.Qty));
            }
            await storeDB.SaveChangesAsync();
        }

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
                token = Base64Encode(tokenResponse.AccessToken);
                Response.Cookies["gfsCheckoutToken"].Value = token;
                Response.Cookies["gfsCheckoutToken"].Expires = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            }
            ViewBag.AccessToken = token;

            var gfsCheckoutInitialPostJson = GetGfsCheckoutPost();

            // define initial post data
            ViewBag.InitialData = Base64Encode(gfsCheckoutInitialPostJson);
            return View("P4MDelivery");
        }

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
                //localCart.CalcTax();
                //result.Shipping = localCart.Shipping;
                //result.Discount = localCart.Discount;
                //result.Tax = localCart.Tax;
                //result.Total = localCart.Total;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet); 
        }

        [HttpGet]
        [Route("p4m/applyDiscountCode/{discountCode}")]
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
                    //result.Discount = localCart.Discount;
                    //result.Shipping = localCart.Shipping;
                    //result.Tax = localCart.Tax;
                    //result.Total = localCart.Total;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("p4m/removeDiscountCode/{discountCode}")]
        public JsonResult RemoveDiscountCode(string discountCode)
        {
            var result = new DiscountMessage();
            try
            {
                var localCart = ShoppingCart.GetCart(this.HttpContext);
                var disc = storeDB.CartDiscounts.Where(d => d.CartId == localCart.ShoppingCartId && d.DiscountCode == discountCode).FirstOrDefault();
                storeDB.CartDiscounts.Remove(disc);
                storeDB.SaveChanges();
                GetCartTotals(result, localCart);
                //localCart.CalcTax();
                result.Code = discountCode;
                result.Amount = localCart.Discount;
                //result.Shipping = localCart.Shipping;
                //result.Tax = localCart.Tax;
                //result.Discount = localCart.Discount;
                //result.Total = localCart.Total;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Route("p4m/itemQtyChanged")]
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
                GetCartTotals(result, localCart);
                //localCart.CalcTax();
                //result.Tax = localCart.Tax;
                //result.Shipping = localCart.Shipping;
                //result.Discount = localCart.Discount;
                //result.Total = localCart.Total;
                //result.Discounts = localCart.Discounts.Select(d => new P4MDiscount { Code = d.DiscountCode, Description = d.Description, Amount = (double)d.Amount }).ToList();
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
        public async Task<JsonResult> Purchase(string cartId, string cvv, decimal cartTotal)
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

                var token = Request.Cookies["p4mToken"].Value;
                _httpClient.SetBearerToken(token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var purchaseMessage = new PostPurchaseMessage { CartId = cartId, CVV = cvv };
                var content = new ObjectContent<PostPurchaseMessage>(purchaseMessage, new JsonMediaTypeFormatter());
                var apiResult = await _httpClient.PostAsync(_urls.BaseApiAddress + "purchase", content);

//                var apiResult = await client.GetAsync(string.Format("{0}purchase/{1}/{2}", _urls.BaseApiAddress, cartId, cvv));
                apiResult.EnsureSuccessStatusCode();
                var messageString = await apiResult.Content.ReadAsStringAsync();
                var purchaseResult = JsonConvert.DeserializeObject<PurchaseResultMessage>(messageString);
                if (!purchaseResult.Success)
                    throw new Exception(purchaseResult.Error);
                // if ACSUrl is blank then the purchase proceeded without 3D Secure
                if (purchaseResult.ACSUrl == null)
                {
                    // purchaseResult includes the transaction Id, auth code and cart 
                    // so the retailer can store whatever is required at this point
                    ShoppingCart.GetCart(this).EmptyCart();
                    HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                    var orderId = await CreateLocalOrderAsync(purchaseResult);
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
        public async Task<JsonResult> PaypalSetup(string cartId, decimal cartTotal, P4MAddress newDropPoint)
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

                var token = Request.Cookies["p4mToken"].Value;
                _httpClient.SetBearerToken(token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var purchaseMessage = new PostPurchaseMessage { CartId = cartId, NewDropPoint = newDropPoint };
                var content = new ObjectContent<PostPurchaseMessage>(purchaseMessage, new JsonMediaTypeFormatter());
                var apiResult = await _httpClient.PostAsync(_urls.BaseApiAddress + "paypalSetup", content);

                //var apiResult = await _httpClient.GetAsync(string.Format("{0}paypalSetup/{1}", _urls.BaseApiAddress, cartId));
                apiResult.EnsureSuccessStatusCode();
                var messageString = await apiResult.Content.ReadAsStringAsync();
                var setupResult = JsonConvert.DeserializeObject<TokenMessage>(messageString);
                if (!setupResult.Success)
                    throw new Exception(setupResult.Error);
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

        public async Task<int> CreateLocalOrderAsync(PurchaseResultMessage purchase)
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
                Country = purchase.DeliverTo.CountryCode,
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
                var ordItem = new OrderDetail
                {
                    ItemId = Convert.ToInt32(item.Sku),
                    OrderId = order.OrderId,
                    Quantity = (int)item.Qty,
                    UnitPrice = (decimal)item.Price
                };
                storeDB.OrderDetails.Add(ordItem);
            }
            storeDB.SaveChanges();
            return order.OrderId;
        }

        // TODO: Build more details into the request!
        public string GetGfsCheckoutPost()
        {
            var localCart = ShoppingCart.GetCart(HttpContext);

            var checkoutRequest = new
            {
                Request = new
                {
                    DateRange = new
                    {
                        DateFrom = String.Format("{0:yyyy-MM-dd}", DateTime.Now),
                        DateTo = String.Format("{0:yyyy-MM-dd}", DateTime.Now.AddDays(14))
                    },
                    Order = new
                    {
                        Transit = new
                        {
                            Recipient = new
                            {
                                Location = new
                                {
                                    CountryCode = new
                                    {
                                        Code = ViewBag.InitialCountryCode, //"GB",
                                        Encoding = "ccISO_3166_1_Alpha2"
                                    },
                                    Postcode = ViewBag.InitialPostCode,// "SO40 7JF", //ViewBag.InitialPostCode,
                                    town = "Soho",
                                    addressLineCollection = new string[] { "AddressLine" },
                                },
                                contactDetails = new
                                {
                                    Email = "test@earsman.com"
                                },
                                Person = new
                                {
                                    firstName = "First",
                                    lastName = "Last",
                                    title = "Title"
                                }
                                
                            }
                        },
                        Value = new
                        {
                            CurrencyCode = "GBP",
                            Value = localCart.GetTotal()
                        }
                    },
                    RequestedDeliveryTypes = new string[] { "dmDropPoint", "dmStandard" },
                    Session = new  // TODO: Remove this when the Open ID connection is in place
                    {
                        APIKeyId = "CL-4CE92613-89A6-4248-A573-A9A7333E6A06",
                        sessionID = ""

                    }
                }
            };

            return Newtonsoft.Json.JsonConvert.SerializeObject(checkoutRequest);

            //return Convert.ToBase64String(Encoding.ASCII.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(checkoutRequest)));
        }

        [HttpGet]
        [Route("p4m/purchaseComplete/{cartId}")]
        public async Task<ActionResult> PurchaseComplete(string cartId)
        {
            var cartMessage = await GetCartFromP4MAsync(cartId);
            if (cartMessage.Success)
            {
                var purchase = new PurchaseResultMessage
                {
                    Cart = cartMessage.Cart,
                    BillTo = cartMessage.BillTo,
                    DeliverTo = cartMessage.DeliverTo
                };
                var orderId = await CreateLocalOrderAsync(purchase);
                ShoppingCart.GetCart(this).EmptyCart();
                HttpContext.Session[ShoppingCart.CartSessionKey] = null;
                return RedirectToAction("Complete", "Checkout", new { id = orderId });
            }
            return View("error");
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
            var apiResult = await _httpClient.GetAsync(string.Format("{0}cart/{1}?wantAddresses=true", _urls.BaseApiAddress, cartId));
            var messageString = await apiResult.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<CartMessage>(messageString);
            if (!result.Success)
                throw new Exception(result.Error);
            return result;
        }

        public string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        public string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

    }
}