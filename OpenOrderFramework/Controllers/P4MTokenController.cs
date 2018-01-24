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
using System.Net.Http.Headers;
using Newtonsoft.Json;
using OpenOrderFramework.Models;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using System.Net.Http.Formatting;
using System.Linq;
using OpenOrderFramework.ViewModels;

namespace OpenOrderFramework.Controllers
{
    public static class P4MHelpers
    {
        static TokenResponse _clientToken = null;
        static DateTime _clientTokenExpires = DateTime.UtcNow;
        static P4MConsts _urls = new P4MConsts();

        public static async Task<TokenResponse> GetClientTokenAsync()
        {
            if (_clientToken == null || _clientTokenExpires < DateTime.UtcNow)
            {
                // get our client token - this can be cached
                var client = new OAuth2Client(new Uri(_urls.TokenEndpoint), _urls.ClientId, _urls.ClientSecret);
                _clientToken = await client.RequestClientCredentialsAsync("p4mRetail");
                _clientTokenExpires = DateTime.UtcNow.AddSeconds(_clientToken.ExpiresIn);
            }
            return _clientToken;
        }

        public static void RemoveCookie(HttpResponseBase response, string cookieName)
        {
            var cookie = response.Cookies[cookieName];
            if (cookie != null)
            {
                cookie.Expires = DateTime.UtcNow.AddDays(-1);
            }
        }
    }

    public class P4MTokenController : Controller
    {
        ApplicationDbContext storeDB = new ApplicationDbContext();
        static HttpClient _httpClient = new HttpClient();
        static P4MConsts _urls = new P4MConsts();
        public P4MTokenController()
        {
            if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public P4MTokenController(ApplicationUserManager userManager)
        {
            UserManager = userManager;
        }

        #region local site helpers
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

        private SignInHelper _helper;
        private SignInHelper SignInHelper
        {
            get
            {
                if (_helper == null)
                {
                    _helper = new SignInHelper(UserManager, AuthenticationManager);
                }
                return _helper;
            }
        }

        private IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.GetOwinContext().Authentication;
            }
        }
        #endregion

        [HttpGet]
        [Route("p4m/checkEmail")]
        public async Task<ActionResult> CheckEmail(string email, string name)
        {
            // this is triggered in guest mode when a consumer enters their email address
            // this endpoint should be loaded in a popup window
            // first we check with P4M for their status:
            // - if known and confirmed, unknown, we close the popup immediately and continue as guest
            // - if known but not confirmed we redirect them to the sign up server to ask them to confirm their email
            try
            {
                var clientToken = await P4MHelpers.GetClientTokenAsync();
                // ready to check
                _httpClient.SetBearerToken(clientToken.AccessToken);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var apiResult = await _httpClient.GetAsync($"{_urls.BaseIdSrvUrl}/consumerStatus/{email}");
                // check the result
                apiResult.EnsureSuccessStatusCode();
                var messageString = await apiResult.Content.ReadAsStringAsync();
                var statusResult = JsonConvert.DeserializeObject<ConsumerStatusMessage>(messageString);
                if (!statusResult.Success)
                    throw new Exception(statusResult.Error);
                if (statusResult.IsGuest)
                {
                    var host = Uri.EscapeDataString("http://localhost:3000/");
                    return Redirect($"{_urls.BaseIdSrvUiUrl}confirmGuest?id={statusResult.UserId}&email={email}&name={name}&host={host}");
                }
                else
                    return View("~/Views/P4M/ClosePopup.cshtml");
            }
            catch (Exception e)
            {
                return View("Error");
            }
        }

        [HttpGet]
        [Route("p4m/signup")]
        public async Task<ActionResult> SignUp()
        {
            // if the user is logged in the we can save their details before redirecting to the SignUp controller
            var result = new LoginMessage();
            try
            {
                var authUser = AuthenticationManager.User;
                if (authUser == null || !authUser.Identity.IsAuthenticated)
                {
                    result.RedirectUrl = _urls.BaseIdSrvUiUrl + "signup";
                }
                else
                {
                    // user is logged in so we can send details to P4M
                    var clientToken = await P4MHelpers.GetClientTokenAsync();
                    // now create a consumer from the local user details
                    var consumer = await GetConsumerFromAppUserAsync(authUser.Identity.GetUserId());
                    // we can also save their most recent cart
                    var cart = GetMostRecentCart(authUser.Identity.GetUserName());
                    // ready to send
                    _httpClient.SetBearerToken(clientToken.AccessToken);
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var registerMessage = new ConsumerAndCartMessage { Consumer = consumer, Cart = cart };
                    var content = new ObjectContent<ConsumerAndCartMessage>(registerMessage, new JsonMediaTypeFormatter());
                    var apiResult = await _httpClient.PostAsync(_urls.BaseApiAddress + "registerConsumer", content);
                    // check the result
                    apiResult.EnsureSuccessStatusCode();
                    var messageString = await apiResult.Content.ReadAsStringAsync();
                    var registerResult = JsonConvert.DeserializeObject<ConsumerIdMessage>(messageString);
                    result.RedirectUrl = registerResult.RedirectUrl;
                }
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                return View("Error");
            }
            return Redirect(result.RedirectUrl); 
        }

        [HttpGet]
        [Route("p4m/loginConfirmedGuest")]
        public ActionResult LoginConfirmedGuest()
        {
            // a guest has just confirmed their registration so we can now sign them in
            Logoff(Response);
            return View("~/Views/P4M/ClosePopupAndLogin.cshtml");
        }

        [HttpGet]
        [Route("p4m/getP4MAccessToken")]
        public ActionResult GetToken()
        {
            // this is all handled by the hidden iframe so just return nothing here
            return Content("<html><body></body></html>");
            // state should be validated here - get from cookie
            //string stateFromCookie, nonceFromCookie;
            //var state = Request.Params.GetValues("p4mState").FirstOrDefault();
            //GetTempState(out stateFromCookie, out nonceFromCookie);
            //P4MHelpers.RemoveCookie(Response, "p4mState");
            //if (state.Equals(stateFromCookie, StringComparison.Ordinal))
            //{
            //    var token = Request.Params.GetValues("access_token").FirstOrDefault();
            //    Response.Cookies["p4mToken"].Value = token;
            //    //Response.Cookies["p4mToken"].Expires = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            //    return View("~/Views/P4M/ClosePopup.cshtml");
            //}
            //// error occurred so try to recover
            //Logoff(Response);
            //return View("~/Views/P4M/ClosePopupAndRefresh.cshtml");
        }

        [HttpPost]
        [Route("p4m/getP4MAccessToken")]
        public ActionResult GetAccessToken()
        {
            // state should be validated here - get from cookie
            string stateFromCookie, nonceFromCookie;
            var state = Request.Params.GetValues("p4mState").FirstOrDefault();
            GetTempState(out stateFromCookie, out nonceFromCookie);
            P4MHelpers.RemoveCookie(Response, "p4mState");
            if (state.Equals(stateFromCookie, StringComparison.Ordinal))
            {
                var token = Request.Params.GetValues("access_token").FirstOrDefault();
                var expiresInStr = Request.Params.GetValues("expires_in").FirstOrDefault();
                int expiresIn = 0;
                int.TryParse(expiresInStr, out expiresIn);
                var expires = DateTime.UtcNow.AddSeconds(expiresIn);
                Response.Cookies["p4mToken"].Value = token;
                Response.Cookies["p4mToken"].Expires = expires;
                Response.Cookies["p4mTokenExpires"].Value = expires.ToString("s") + "Z";
                Response.Cookies["p4mTokenExpires"].Expires = expires;
                return View("~/Views/P4M/ClosePopup.cshtml");
            }
            // error occurred so try to recover
            Logoff(Response);
            return View("~/Views/P4M/ClosePopupAndRefresh.cshtml");
        }

        //[HttpGet]
        //[Route("p4m/isLocallyLoggedIn")]
        //public JsonResult IsLocallyLoggedIn()
        //{
        //    var result = new P4MBaseMessage();
        //    if (P4MConsts.CheckoutMode != CheckoutMode.Exclusive)
        //    {
        //        var authUser = AuthenticationManager.User;
        //        if (authUser == null || !authUser.Identity.IsAuthenticated)
        //            result.Error = "Not logged in";
        //        this.Response.Cookies["p4mLocalLogin"].Value = result.Success ? "true" : "false";
        //    }
        //    else
        //    {
        //        var hasToken = this.Response.Cookies.AllKeys.Contains("p4mToken") && !string.IsNullOrWhiteSpace(this.Response.Cookies["p4mLocalLogin"].Value);
        //        this.Response.Cookies["p4mLocalLogin"].Value = hasToken ? "true" : "false";
        //    }
        //    return Json(result, JsonRequestBehavior.AllowGet);
        //}

        [HttpGet]
        [Route("p4m/localLogin")]
        public async Task<JsonResult> LocalLogin(string currentPage)
        {
            this.Response.Cookies["p4mLocalLogin"].Value = "false";
            var result = new LoginMessage();
            try
            {
                var token = this.Request.Cookies["p4mToken"].Value;
                var hasOpenCart = await LocalConsumerLoginAsync(token);
                this.Response.Cookies["p4mOfferCartRestore"].Value = hasOpenCart ? "true" : "false";
                if (currentPage.ToLower().Contains("/account/login"))
                    result.RedirectUrl = "/p4m/checkout";
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                Logoff(Response);
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Route("p4m/localLogin")]
        public async Task<JsonResult> LocalLogin(LoginConsumerMessage message)
        {
            this.Response.Cookies["p4mLocalLogin"].Value = "false";
            var result = new LoginMessage();
            try
            {
                result.LocalId = await LocalConsumerLoginAsync(message.Consumer);
                if (message.CurrentPage.ToLower().Contains("/account/login"))
                    result.RedirectUrl = "/p4m/checkout";
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                Logoff(Response);
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        async Task<string> LocalConsumerLoginAsync(Consumer consumer)
        {
            // Check if there is a local ID and login. 
            // If not try to match on Email. If found then store local ID for consumer and login.
            // If not local ID or Email then create a new user and store the new local ID

            // is there a logged in user already?
            string authUserId = null;
            var alreadyLoggedIn = false;
            var authUser = AuthenticationManager.User;
            if (authUser != null && authUser.Identity.IsAuthenticated)
            {
                authUserId = authUser.Identity.GetUserId();
                alreadyLoggedIn = true;
            }
            // get the local Id from P4M if possible
            string p4mLocalId = null;
            if (consumer.Extras != null && consumer.Extras.ContainsKey("localId"))
            {
                p4mLocalId = consumer.Extras["localId"];
                if (alreadyLoggedIn && p4mLocalId != authUserId)
                {
                    // switching local users here
                    AuthenticationManager.SignOut();
                    authUserId = null;
                    authUser = null;
                    alreadyLoggedIn = false;
                }
            }
            
            // if P4M already has a localId we use that, otherwise we use the Id of the logged in user (if any)
            // update the local user details with the current P4M details - this will create a new user if required
            ApplicationUser appUser = await GetAppUserAsync(consumer, p4mLocalId ?? authUserId);
            
            // if this is a new local user then appUser.PasswordHash will be null
            if (alreadyLoggedIn || appUser.PasswordHash != null)
            {
                if (!alreadyLoggedIn)
                    await LocalLoginAsync(appUser);
                this.Response.Cookies["p4mLocalLogin"].Value = "true";
#pragma warning disable 4014
                // save any possible changes to the local user 
                UserManager.UpdateAsync(appUser);
                // store the local Id if not already stored
#pragma warning restore 4014
                return appUser.Id;
            }
            // local user has NOT been found for the P4M consumer so we need to create one and log them in
            var idResult = await UserManager.CreateAsync(appUser);
            if (idResult.Succeeded)
            {
                appUser = await UserManager.FindByEmailAsync(consumer.Email);
                var password = GeneratePassword();
                await UserManager.AddPasswordAsync(appUser.Id, password);
                await LocalLoginAsync(appUser);
                this.Response.Cookies["p4mLocalLogin"].Value = "true";
            }
            return appUser.Id;
        }
        
        async Task<bool> LocalConsumerLoginAsync(string token)
        {
            // get the consumer's details from P4M. 
            // Check if there is a local ID and login. 
            // If not try to match on Email. If found then store local ID for consumer and login.
            // If not local ID or Email then create a new user and store the new local ID
            var consumerResult = await GetConsumerAsync(token);
            if (consumerResult == null || !consumerResult.Success)
                throw new Exception(consumerResult.Error ?? "Could not retrieve your details");
            var consumer = consumerResult.Consumer;

            var tokenExpires = Helpers.GetTokenExpiry(token);
            Response.Cookies["p4mAvatarUrl"].Value = consumer.ProfilePicUrl;
            Response.Cookies["p4mAvatarUrl"].Expires = tokenExpires;
            Response.Cookies["p4mGivenName"].Value = consumer.GivenName;
            Response.Cookies["p4mGivenName"].Expires = tokenExpires;

            //if (consumer.PrefDeliveryAddress != null)
            //{
            //    var addr = consumer.PrefDeliveryAddress;
            //    Response.Cookies["p4mConsumer"].Value =
            //        consumer.Id + "|" + addr.Id + "|" + addr.Street1 + "|" + addr.Street2 + "|" + addr.City + "|" +
            //        addr.PostCode + "|" + addr.State + "|" + addr.CountryCode + "|" +
            //        addr.Latitude + "|" + addr.Longitude + "|" + consumer.DeliveryPreferences;
            //    Response.Cookies["p4mConsumer"].Expires = tokenExpires;
            //}

            // is there a logged in user already?
            string authUserId = null;
            var alreadyLoggedIn = false;
            var authUser = AuthenticationManager.User;
            if (authUser != null && authUser.Identity.IsAuthenticated)
            {
                authUserId = authUser.Identity.GetUserId();
                alreadyLoggedIn = true;
            }
            // get the local Id from P4M if possible
            string p4mLocalId = null;
            if (consumer.Extras != null && consumer.Extras.ContainsKey("localId"))
            {
                p4mLocalId = consumer.Extras["localId"];
                if (alreadyLoggedIn && p4mLocalId != authUserId)
                {
                    // switching local users here
                    AuthenticationManager.SignOut();
                    authUserId = null;
                    authUser = null;
                    alreadyLoggedIn = false;
                }
            }

            // if P4M already has a localId we use that, otherwise we use the Id of the logged in user (if any)
            // update the local user details with the current P4M details - this will create a new user if required
            ApplicationUser appUser = await GetAppUserAsync(consumer, p4mLocalId ?? authUserId);

            // if this is a new local user then appUser.PasswordHash will be null
            if (alreadyLoggedIn || appUser.PasswordHash != null)
            {
                if (!alreadyLoggedIn)
                    await LocalLoginAsync(appUser);
                this.Response.Cookies["p4mLocalLogin"].Value = "true";
#pragma warning disable 4014
                // save any possible changes to the local user 
                UserManager.UpdateAsync(appUser);
                // store the local Id if not already stored
                if (p4mLocalId == null)
                    // NB. we're only saving the local Id the first time the user visits the site
                    SaveLocalIdAsync(token, appUser.Id);
#pragma warning restore 4014
                return consumerResult.HasOpenCart;
            }
            // local user has NOT been found for the P4M consumer so we need to create one and log them in
            var idResult = await UserManager.CreateAsync(appUser);
            if (idResult.Succeeded)
            {
                appUser = await UserManager.FindByEmailAsync(consumer.Email);
                var password = GeneratePassword();
                await UserManager.AddPasswordAsync(appUser.Id, password);
                await LocalLoginAsync(appUser);
                this.Response.Cookies["p4mLocalLogin"].Value = "true";
#pragma warning disable 4014
                // NB. we're only saving the local Id the first time the user visits the site
                SaveLocalIdAsync(token, appUser.Id);
#pragma warning restore 4014
            }
            return consumerResult.HasOpenCart;
        }

        async Task<Consumer> GetConsumerFromAppUserAsync(string localId)
        {
            ApplicationUser appUser = await UserManager.FindByIdAsync(localId);
            var consumer = new Consumer {
                Email = appUser.Email,
                GivenName = appUser.FirstName,
                FamilyName = appUser.LastName,
                Language = "EN",
                PreferredCurrency = "GBP"
            };
            var address = new P4MAddress {
                Street1 = appUser.Address,
                City = appUser.City,
                State = appUser.State,
                PostCode = appUser.PostalCode,
                Country = appUser.Country,
                AddressType = "Address",
                CountryCode = "UK",
                Label = "Home"                
            };
            consumer.Addresses = new List<P4MAddress> { address };
            consumer.Extras = new Dictionary<string, string>();
            consumer.Extras.Add("localId", localId);
            return consumer;
        }

        [HttpPost]
        [Route("p4m/localLogout")]
        public ActionResult LocalLogout(string logoutToken)
        {
            if (logoutToken == P4MConsts.LogoutToken)
            {
                AuthenticationManager.SignOut();
                Logoff(this.Response);
            }
            return RedirectToAction("Index", "Home");
        }

        P4MCart GetMostRecentCart(string localUsername)
        {
            var order = storeDB.Orders.Where(o => o.Username == localUsername).OrderByDescending(o => o.OrderDate).FirstOrDefault();
            if (order == null)
                return null;
            var p4mCart = new P4MCart
            {
                OrderId = order.OrderId.ToString(),
                SessionId = "Register",
                Date = order.OrderDate,
                PaymentType = "DB",
                Currency = "GBP",
                ShippingAmt = (double)order.Shipping,
                Tax = (double)order.Tax,
                Items = new List<P4MCartItem>()
            };
            var items = storeDB.OrderDetails.Where(i => i.OrderId == order.OrderId);
            foreach (var cartItem in items)
            {
                var item = cartItem.Item;// storeDB.Items.Single(i => i.ID == cartItem.ItemId);
                p4mCart.Items.Add(new P4MCartItem
                {
                    Make = item.Name,
                    Sku = item.ID.ToString(),
                    Desc = item.Name,
                    Qty = cartItem.Quantity,
                    Price = (double)item.Price,
                    LinkToImage = item.ItemPictureUrl,
                });
            }
            var discounts = storeDB.OrderDiscounts.Where(d => d.OrderId == order.OrderId);
            foreach (var disc in discounts)
            {
                if (p4mCart.Discounts == null)
                    p4mCart.Discounts = new List<P4MDiscount>();
                p4mCart.Discounts.Add(new P4MDiscount { Code = disc.DiscountCode.ToString(), Description = disc.Description, Amount = (double)disc.Amount });
            }
            return p4mCart;
        }

        async Task<ApplicationUser> GetAppUserAsync(Consumer consumer, string localId)
        {
            var address = consumer.PrefDeliveryAddress;
            ApplicationUser appUser = null;
            if (localId != null)
                appUser = await UserManager.FindByIdAsync(localId);
            if (appUser == null)
                appUser = await UserManager.FindByEmailAsync(consumer.Email);
            if (appUser == null)
                appUser = new ApplicationUser();
            appUser.UserName = consumer.Email;
            appUser.Email = consumer.Email;
            appUser.EmailConfirmed = true;
            appUser.FirstName = consumer.GivenName;
            appUser.LastName = consumer.FamilyName;
            if (address != null)
            {
                appUser.Address = address.Street1 ?? address.Street2;
                appUser.City = address.City;
                appUser.State = address.State;
                appUser.PostalCode = address.PostCode;
                appUser.Country = address.Country;
            }
            appUser.LockoutEnabled = false;
            appUser.Phone = consumer.MobilePhone;
            appUser.PhoneNumber = consumer.MobilePhone;
            return appUser;
        }

        const int passLength = 20;
        const string validChars = "abcdefghijklmnozABCDEFGHIJKLMNOZ1234567890!@#$%^&*()-=";
        Random random = new Random();
        string GeneratePassword()
        {
            return "Password1";
            //StringBuilder strB = new StringBuilder(100);
            //int i = 0;
            //while (i++ < passLength)
            //{
            //    strB.Append(validChars[random.Next(validChars.Length)]);
            //}
            //return strB.ToString();
        }

        async Task LocalLoginAsync(ApplicationUser user)
        {
            var identity = await UserManager.CreateIdentityAsync(user, DefaultAuthenticationTypes.ApplicationCookie);
            // allow cookie to expire at the end of the browser session
            AuthenticationManager.SignIn(new AuthenticationProperties (), new ClaimsIdentity(identity));
        }

        async Task<ConsumerMessage> GetConsumerAsync(string token)
        {
            // get the consumer's details from P4M. 
            _httpClient.SetBearerToken(token);
            var result = await _httpClient.GetAsync(_urls.BaseApiAddress + "consumer?checkHasOpenCart=true");
            var messageString = await result.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ConsumerMessage>(messageString);
        }

        async Task SaveLocalIdAsync(string token, string id)
        {
            // save the local consumer Id as an "extra" in P4M 
            _httpClient.SetBearerToken(token);
            string json = "{'LocalId':'"+id+"','Tags':'tag1,tag2'}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var result = await _httpClient.PostAsync(_urls.BaseApiAddress + "consumerExtras", content);
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<P4MBaseMessage>(messageString);
            if (!message.Success)
                throw new Exception(message.Error);
        }

        async Task<string> GetSigningCertAsync()
        {
            if (_urls.SigningCert == null)
            {
                var result = await _httpClient.GetAsync(_urls.JwksUrl);
                var messageString = await result.Content.ReadAsStringAsync();
                dynamic jwks = JsonConvert.DeserializeObject(messageString);
                _urls.SigningCert = jwks.keys[0].x5c[0];
            }
            return _urls.SigningCert;
        }

        async Task<bool> ValidateToken(string token, string nonce)
        {
            var x509Str = await GetSigningCertAsync();
            var cert = new X509Certificate2(Convert.FromBase64String(x509Str));
            var parameters = new TokenValidationParameters
            {
                ValidAudience = _urls.ClientId,
                ValidIssuers = new List<string> { _urls.BaseIdSrvUrl, "https://parcelfor.me", "https://dev.parcelfor.me" },
                IssuerSigningToken = new X509SecurityToken(cert)
            };

            SecurityToken jwt;
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out jwt);

            // validate nonce
            var nonceClaim = principal.FindFirst("nonce");

            P4MHelpers.RemoveCookie(Response, "p4mNonce");
            if (!string.Equals(nonceClaim.Value, nonce, StringComparison.Ordinal))
                throw new Exception("invalid nonce");
            return true;
        }

        //private string ParsedJwt(string token)
        //{
        //    if (!token.Contains("."))
        //    {
        //        return token;
        //    }

        //    var parts = token.Split('.');
        //    var part = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));

        //    var jwt = JObject.Parse(part);
        //    return jwt.ToString();
        //}

        public static void Logoff(HttpResponseBase response)
        {
            // clear the local P4M cookies
            P4MHelpers.RemoveCookie(response, "p4mToken");
            P4MHelpers.RemoveCookie(response, "p4mTokenExpires");
            P4MHelpers.RemoveCookie(response, "p4mAvatarUrl");
            P4MHelpers.RemoveCookie(response, "p4mGivenName");
            P4MHelpers.RemoveCookie(response, "p4mLocalLogin");
            P4MHelpers.RemoveCookie(response, "p4mState");
            P4MHelpers.RemoveCookie(response, "p4mNonce");
            P4MHelpers.RemoveCookie(response, "p4mOfferCartRestore");
            P4MHelpers.RemoveCookie(response, "p4mLocale");
            P4MHelpers.RemoveCookie(response, "p4mConsumer");
            P4MHelpers.RemoveCookie(response, "p4mPrefAddress");
            P4MHelpers.RemoveCookie(response, "p4mCart");
            P4MHelpers.RemoveCookie(response, "p4mCartAddress");
            P4MHelpers.RemoveCookie(response, "gfsCheckoutToken");
        }

        void GetTempState(out string state, out string nonce)
        {
            state = null;
            nonce = null;
            if (Request.Cookies["p4mState"] != null)
            {
                state = this.Request.Cookies["p4mState"].Value;
                nonce = this.Request.Cookies["p4mNonce"].Value;
            }
        }
    }
}