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
    public static class P4MConstants
    {
        //public const string ClientId = "10004";
        //public const string ClientSecret = "secret";
        //public const string AppMode = "dev";

        //public const string BaseAddress = "https://"+AppMode+".parcelfor.me:44333";
        //public const string BaseApiAddress = "https://" + AppMode + ".parcelfor.me:44321/api/v2/";
        //public const string IdSrvUrl = BaseAddress + "/ui/";
        //public const string LocalCallbackUrl = "http://localhost:3000/p4m/getP4MAccessToken";
        //public const string TokenEndpoint = BaseAddress + "/connect/token";
    }

    public class P4MTokenController : Controller
    {
        ApplicationDbContext storeDB = new ApplicationDbContext();
        static HttpClient _httpClient = new HttpClient();
        static P4MUrls _urls = new P4MUrls();
        public P4MTokenController()
        {
        }

        public P4MTokenController(ApplicationUserManager userManager)
        {
            UserManager = userManager;
        }

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

        static TokenResponse _clientToken = null;
        
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
                    result.RedirectUrl = _urls.IdSrvUrl + "signup";
                }
                else
                {
                    // user is logged in so we can send details to P4M
                    if (_clientToken == null)
                    {
                        // get our client token - this can be cached
                        var client = new OAuth2Client(new Uri(_urls.TokenEndpoint), _urls.ClientId, _urls.ClientSecret);
                        _clientToken = await client.RequestClientCredentialsAsync("p4mRetail");
                    }

                    // now create a consumer from the local user details
                    var consumer = await GetConsumerFromAppUserAsync(authUser.Identity.GetUserId());
                    // we can also save their most recent cart
                    var cart = GetMostRecentCart(authUser.Identity.GetUserName());
                    // ready to send
                    _httpClient.SetBearerToken(_clientToken.AccessToken);
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var registerMessage = new ConsumerAndCartMessage { Consumer = consumer, Cart = cart };
                    var content = new ObjectContent<ConsumerAndCartMessage>(registerMessage, new JsonMediaTypeFormatter());
                    var apiResult = await _httpClient.PostAsync(_urls.BaseApiAddress + "registerConsumer", content);
                    // check the result
                    apiResult.EnsureSuccessStatusCode();
                    var messageString = await apiResult.Content.ReadAsStringAsync();
                    var registerResult = JsonConvert.DeserializeObject<ConsumerIdMessage>(messageString);
                    if (!registerResult.Success)
                    {
                        if (registerResult.Error.Contains("registered"))
                            result.RedirectUrl = $"{_urls.IdSrvUrl}alreadyRegistered?firstName={consumer.GivenName}&email={consumer.Email}";
                        else
                            result.RedirectUrl = $"{_urls.IdSrvUrl}signupError?firstName={consumer.GivenName}&error={registerResult.Error}";
                    }
                    else
                        result.RedirectUrl = $"{_urls.IdSrvUrl}registerConsumer/{registerResult.ConsumerId}";
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
        [Route("p4m/getP4MAccessToken")]
        public async Task<ActionResult> GetToken(string code, string state)
        {
            // state should be validated here - get from cookie
            string stateFromCookie, nonceFromCookie;
            GetTempState(out stateFromCookie, out nonceFromCookie);
            if (!state.Equals(stateFromCookie, StringComparison.Ordinal))
                throw new Exception("Invalid state returned from ID server");
            this.Response.Cookies["p4mState"].Expires = DateTime.UtcNow;

            var client = new OAuth2Client(new Uri(_urls.TokenEndpoint), _urls.ClientId, _urls.ClientSecret);
            var tokenResponse = await client.RequestAuthorizationCodeAsync(code, _urls.RedirectUrl);
            if (!tokenResponse.IsHttpError && ValidateToken(tokenResponse.IdentityToken, nonceFromCookie) && !string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                //var parsedToken = ParseJwt(response.AccessToken);
                this.Response.Cookies["p4mToken"].Value = tokenResponse.AccessToken;
                this.Response.Cookies["p4mToken"].Expires = DateTime.UtcNow.AddYears(1);
                //PostXMLData();
                return View("ReturnToken");
            }
            return View("error");
        }

        [HttpGet]
        [Route("p4m/isLocallyLoggedIn")]
        public JsonResult IsLocallyLoggedIn()
        {
            var result = new P4MBaseMessage();
            var authUser = AuthenticationManager.User;
            if (authUser == null || !authUser.Identity.IsAuthenticated)
                result.Error = "Not logged in";
            this.Response.Cookies["p4mLocalLogin"].Value = result.Success ? "true" : "false";
            return Json(result, JsonRequestBehavior.AllowGet);
        }

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
                    result.RedirectUrl = "/checkout/p4mCheckout";
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                Logoff(Response);
            }
            return Json(result, JsonRequestBehavior.AllowGet);
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

            this.Response.Cookies["p4mAvatarUrl"].Value = consumer.ProfilePicUrl;
            this.Response.Cookies["p4mAvatarUrl"].Expires = DateTime.UtcNow.AddYears(1);
            this.Response.Cookies["p4mGivenName"].Value = consumer.GivenName;
            this.Response.Cookies["p4mGivenName"].Expires = DateTime.UtcNow.AddYears(1);
            this.Response.Cookies["p4mDefaultPostCode"].Value = consumer.PrefDeliveryAddress?.PostCode;
            this.Response.Cookies["p4mDefaultPostCode"].Expires = consumer.PrefDeliveryAddress == null ? DateTime.UtcNow : DateTime.UtcNow.AddYears(1);
            this.Response.Cookies["p4mDefaultCountryCode"].Value = consumer.PrefDeliveryAddress?.CountryCode;
            this.Response.Cookies["p4mDefaultCountryCode"].Expires = consumer.PrefDeliveryAddress == null ? DateTime.UtcNow : DateTime.UtcNow.AddYears(1);

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
            if (consumer.Extras != null && consumer.Extras.ContainsKey("LocalId"))
            {
                p4mLocalId = consumer.Extras["LocalId"];
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
            consumer.Extras.Add("LocalId", localId);
            return consumer;
        }

        P4MCart GetMostRecentCart(string localUsername)
        {
            var order = storeDB.Orders.Where(o => o.Username == localUsername).OrderByDescending(o => o.OrderDate).FirstOrDefault();
            if (order == null)
                return null;
            var p4mCart = new P4MCart
            {
                Reference = order.OrderId.ToString(),
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
            StringBuilder strB = new StringBuilder(100);
            int i = 0;
            while (i++ < passLength)
            {
                strB.Append(validChars[random.Next(validChars.Length)]);
            }
            return strB.ToString();
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
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var result = await _httpClient.GetAsync(_urls.BaseApiAddress + "consumer?checkHasOpenCart=true");
            var messageString = await result.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ConsumerMessage>(messageString);
        }

        async Task SaveLocalIdAsync(string token, string id)
        {
            // save the local consumer Id as an "extra" in P4M 
            _httpClient.SetBearerToken(token);
            string json = "{\"LocalId\":\""+id+"\"}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var result = await _httpClient.PostAsync(_urls.BaseApiAddress + "consumerExtras", content);
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<P4MBaseMessage>(messageString);
            if (!message.Success)
                throw new Exception(message.Error);
        }

        bool ValidateToken(string token, string nonce)
        {
            // this x509 string is taken from https://id.parcelfor.me:44333/core/.well-known/jwks "x5c" - if using a different server then might need to get a different cert from that server
//            var x509Str = "MIIFKTCCBBGgAwIBAgIQBpXgM4drGUyWvFilY6mQ/DANBgkqhkiG9w0BAQsFADBNMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMScwJQYDVQQDEx5EaWdpQ2VydCBTSEEyIFNlY3VyZSBTZXJ2ZXIgQ0EwHhcNMTUwMzMwMDAwMDAwWhcNMTYwNDA2MTIwMDAwWjB3MQswCQYDVQQGEwJHQjEUMBIGA1UECBMLV2VzdCBTdXNzZXgxEDAOBgNVBAcTB0NyYXdsZXkxGjAYBgNVBAoTEVBhcmNlbCBGb3IgTWUgTHRkMQswCQYDVQQLEwJJVDEXMBUGA1UEAwwOKi5wYXJjZWxmb3IubWUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDuGud1W2It2TficmFGyflPx59t1zWOU0gjAKh118kV0TnrWM5V1dApFaRwIrnehHhOvQnAsACYV0K3T5ZlMPw3wWv8XF56SGrZHJx3SPlZEIl9UP2J4yU82Fyz8YIIzbdlEFJnQ/5bNkX/qmcL6oFjpkxalHiW04OZwh0fe7XnpvYmRiMP2x1Mss35cArTGagJA8jy2dLIkIp4x0fCng3pPzlNLlIEN1q2ERnuOfY26jJgh5AMoy8POMX2LrcH3HWI106yW4wlYqLGVyVAovx4+D4VJgqB/7Bg1uPKIG12hwMkX/sva/QLbdy3vmptHOn0l+RY4WOlveOVFKfPljnjAgMBAAGjggHZMIIB1TAfBgNVHSMEGDAWgBQPgGEcgjFh1S8o541GOLQs4cbZ4jAdBgNVHQ4EFgQUbM31cW4e631fz227xlUJwZE2UFYwJwYDVR0RBCAwHoIOKi5wYXJjZWxmb3IubWWCDHBhcmNlbGZvci5tZTAOBgNVHQ8BAf8EBAMCBaAwHQYDVR0lBBYwFAYIKwYBBQUHAwEGCCsGAQUFBwMCMGsGA1UdHwRkMGIwL6AtoCuGKWh0dHA6Ly9jcmwzLmRpZ2ljZXJ0LmNvbS9zc2NhLXNoYTItZzMuY3JsMC+gLaArhilodHRwOi8vY3JsNC5kaWdpY2VydC5jb20vc3NjYS1zaGEyLWczLmNybDBCBgNVHSAEOzA5MDcGCWCGSAGG/WwBATAqMCgGCCsGAQUFBwIBFhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMHwGCCsGAQUFBwEBBHAwbjAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEYGCCsGAQUFBzAChjpodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRTSEEyU2VjdXJlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBALDPzKumlhai9D/EJxbwiHxfsZLDxGuoUXNp5gnL2TlE6e0S4HT9wGtTbfL4G+lp6ppxvJN+1ojKC31MuZro7z7s+k1ZZtQVGKucRxN95TQBUmv3gRu4C1zWFYoxCd8k1VZ77sKSOngQ7V7c9EIakA9Q5zfNo6vAcQzff0mMRfTHCNrZV5t9WQxi2HBLu3OLt98QZ8bJ31oGRR6pmAkKnhHzKc6qd8THn2WfEt5rFBRQ5ts785ZU+kOAMvVv0i1cwmMJYo9nySmkXd4EP3YZcHxSvESSBFwOhpVr6Gez+33nAR8hYj10Upv8SHtLYxouqz1EItDj/iXmLhSD4HLdkqg=";
            // this x509 string is taken from https://dev.parcelfor.me:44333/core/.well-known/jwks "x5c" - if using a different server then might need to get a different cert from that server
            var x509Str = "MIIFJjCCBA6gAwIBAgIQCs3kGwA/0NT7FDqMWVclDzANBgkqhkiG9w0BAQsFADBNMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMScwJQYDVQQDEx5EaWdpQ2VydCBTSEEyIFNlY3VyZSBTZXJ2ZXIgQ0EwHhcNMTYwMzMxMDAwMDAwWhcNMTcwNDE3MTIwMDAwWjBqMQswCQYDVQQGEwJHQjEUMBIGA1UECBMLV2VzdCBTdXNzZXgxEDAOBgNVBAcTB0NyYXdsZXkxGjAYBgNVBAoTEVBhcmNlbCBGb3IgTWUgTHRkMRcwFQYDVQQDDA4qLnBhcmNlbGZvci5tZTCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBANJ4iwa49AoPgZQPpZ7FBOXl26VIvAIOFGgKVClI0atUXLS04hBFjifgzckDtWMFt8XX42wu0J3W8ERsetJnwrGrHUOhTnrESqU/H+6dS6lrSnPShBHplHxZIcMITVLEl5DsYBysBAMLEy38Ycy9mU4/jJDvHwBfmCy1DMBAh4wyePqqZVhed5daI+p9DFCwtsWT7ORCoE/WAkDeBBK73Wd2wYvRaQqnEWoNt3G+B71FLjNna8Jt8n5fRaRT16RFDISISWbALeO9lRfpOzDYQ2XNb5zvVCT6QZAX49ke7xFfpj0R2o70FX53l4pqUHEjjYJ1N5UMMOnBuZDXeYYFAakCAwEAAaOCAeMwggHfMB8GA1UdIwQYMBaAFA+AYRyCMWHVLyjnjUY4tCzhxtniMB0GA1UdDgQWBBRlsWweAbsWzTLzPIzbEXKYpg/+rjAnBgNVHREEIDAegg4qLnBhcmNlbGZvci5tZYIMcGFyY2VsZm9yLm1lMA4GA1UdDwEB/wQEAwIFoDAdBgNVHSUEFjAUBggrBgEFBQcDAQYIKwYBBQUHAwIwawYDVR0fBGQwYjAvoC2gK4YpaHR0cDovL2NybDMuZGlnaWNlcnQuY29tL3NzY2Etc2hhMi1nNS5jcmwwL6AtoCuGKWh0dHA6Ly9jcmw0LmRpZ2ljZXJ0LmNvbS9zc2NhLXNoYTItZzUuY3JsMEwGA1UdIARFMEMwNwYJYIZIAYb9bAEBMCowKAYIKwYBBQUHAgEWHGh0dHBzOi8vd3d3LmRpZ2ljZXJ0LmNvbS9DUFMwCAYGZ4EMAQICMHwGCCsGAQUFBwEBBHAwbjAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEYGCCsGAQUFBzAChjpodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRTSEEyU2VjdXJlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBABvdWOTOhnslGMxkosvsKlsYNRPVjEmLMvO1YaKdW6TuKvHl64Hmj+KcW8HbiACuijnlcdk/AxVmVxAMDmw+V38494SysF/xSmVqyp9pPIcGTq10mc/GivfRBfdhU06kP+X3vo7iJi5aUDBm5OkZ233dhKYpXFXuPs9lrEeHSAdctQ278wIxPkjTAS9baRnevByAEmUC6ILIfjQA30LWdrvpl/IAtAQDhjqFW/Eju70S7ckb8720Km/u5P697dGQLanmcS73URzcbjJ7vgYI5YHIMj1LUGSS/RJEj/w5RlJ0mELxvkLhhIUrhC2riAbXWmkiaOk+aHQ2es13R1BvzII=";
            var cert = new X509Certificate2(Convert.FromBase64String(x509Str));

            var parameters = new TokenValidationParameters
            {
                ValidAudience = _urls.ClientId,
                ValidIssuers = new List<string> { _urls.BaseIdSrvUrl, "https://parcelfor.me" },
                IssuerSigningToken = new X509SecurityToken(cert)
            };

            SecurityToken jwt;
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out jwt);

            // validate nonce
            var nonceClaim = principal.FindFirst("nonce");

            if (!string.Equals(nonceClaim.Value, nonce, StringComparison.Ordinal))
                throw new Exception("invalid nonce");
            this.Response.Cookies["p4mNonce"].Expires = DateTime.UtcNow;
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
            response.Cookies["p4mToken"].Value = string.Empty;
            response.Cookies["p4mToken"].Expires = DateTime.UtcNow;
            response.Cookies["p4mAvatarUrl"].Value = string.Empty;
            response.Cookies["p4mAvatarUrl"].Expires = DateTime.UtcNow;
            response.Cookies["p4mGivenName"].Value = string.Empty;
            response.Cookies["p4mGivenName"].Expires = DateTime.UtcNow;
            response.Cookies["p4mLocalLogin"].Value = string.Empty;
            response.Cookies["p4mLocalLogin"].Expires = DateTime.UtcNow;
            response.Cookies["p4mDefaultPostCode"].Value = string.Empty;
            response.Cookies["p4mDefaultPostCode"].Expires = DateTime.UtcNow;
            response.Cookies["p4mDefaultCountry"].Value = string.Empty;
            response.Cookies["p4mDefaultCountry"].Expires = DateTime.UtcNow;
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