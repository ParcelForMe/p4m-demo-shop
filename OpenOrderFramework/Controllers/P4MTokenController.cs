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

namespace OpenOrderFramework.Controllers
{
    public static class P4MConstants
    {
        public const string ClientId = "p4m-login-test";
        public const string ClientSecret = "123456";
        public const string CredClientId = "p4m-login-test";
        public const string CredClientSecret = "123456";
        public const string ClientGuestId = "codeclient_guest";

        //public const string BaseAddress = "https://local.parcelfor.me:44333/core";
        //public const string BaseApiAddress = "https://local.parcelfor.me:44321/api/v2/";
        public const string BaseAddress = "https://dev.parcelfor.me:44333/core";
        public const string BaseApiAddress = "https://dev.parcelfor.me:44321/api/v2/";
        public const string LocalCallbackUrl = "http://localhost:3000/getP4MAccessToken";

        public const string AuthorizeEndpoint = BaseAddress + "/connect/authorize";
        public const string LogoutEndpoint = BaseAddress + "/connect/endsession";
        public const string TokenEndpoint = BaseAddress + "/connect/token";
        public const string UserInfoEndpoint = BaseAddress + "/connect/userinfo";
        public const string IdentityTokenValidationEndpoint = BaseAddress + "/connect/identitytokenvalidation";
        public const string SetPasswordUrl = BaseAddress + "/resetPassword?userId={0}&code={1}&signin=register&clientId={2}";
    }

    public class P4MTokenController : Controller
    {
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
        
        [HttpGet]
        [Route("getP4MAccessToken")]
        public async Task<ActionResult> GetToken(string code, string state)
        {
            // state should be validated here - get from cookie
            string stateFromCookie, nonceFromCookie;
            GetTempState(out stateFromCookie, out nonceFromCookie);
            if (!state.Equals(stateFromCookie, StringComparison.Ordinal))
                throw new Exception("Invalid state returned from ID server");
            this.Response.Cookies["p4mState"].Expires = DateTime.UtcNow;

            var client = new OAuth2Client(new Uri(P4MConstants.TokenEndpoint), P4MConstants.ClientId, P4MConstants.ClientSecret);
            var tokenResponse = await client.RequestAuthorizationCodeAsync(code, P4MConstants.LocalCallbackUrl);
            if (ValidateToken(tokenResponse.IdentityToken, nonceFromCookie) && !string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                //var parsedToken = ParseJwt(response.AccessToken);
                this.Response.Cookies["p4mToken"].Value = tokenResponse.AccessToken;
                this.Response.Cookies["p4mToken"].Expires = DateTime.UtcNow.AddYears(1);
                //await LocalConsumerLogin(tokenResponse.AccessToken);
                return View("ReturnToken");
            }
            return View("error");
        }

        [HttpGet]
        [Route("localLogin")]
        public async Task<JsonResult> LocalLogin(string currentPage)
        {
            var result = new LoginMessage();
            try
            {
                var token = this.Request.Cookies["p4mToken"].Value;
                await LocalConsumerLogin(token);
                if (currentPage.ToLower().Contains("/account/login"))
                    result.RedirectUrl = "/checkout/p4mCheckout";
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        async Task LocalConsumerLogin(string token)
        {
            // get the consumer's details from P4M. 
            // Check if there is a local ID and login. 
            // If not try to match on Email. If found then store local ID for consumer and login.
            // If not local ID or Email then create a new user and store the new local ID
            var consumer = await GetConsumerAsync(token);
            this.Response.Cookies["p4mAvatarUrl"].Value = consumer.ProfilePicUrl;
            this.Response.Cookies["p4mAvatarUrl"].Expires = DateTime.UtcNow.AddYears(1);
            var localId = string.Empty;
            if (consumer.Extras != null && consumer.Extras.ContainsKey("LocalId"))
                localId = consumer.Extras["LocalId"];
            var address = consumer.PrefDeliveryAddress;
            // update the local user details with the current P4M details
            ApplicationUser user = await UserManager.FindByEmailAsync(consumer.Email);
            if (user == null)
                user = new ApplicationUser();
            user.UserName = consumer.Email;
            user.Email = consumer.Email;
            user.EmailConfirmed = true;
            user.FirstName = consumer.GivenName;
            user.LastName = consumer.FamilyName;
            user.Address = address.Street1 ?? address.Street2;
            user.City = address.City;
            user.State = address.State;
            user.PostalCode = address.PostCode;
            user.Country = address.Country;
            user.LockoutEnabled = false;
            user.Phone = consumer.MobilePhone;
            user.PhoneNumber = consumer.MobilePhone;
            if (await LocalLoginAsync(token, localId, consumer.Email))
            {
                await UserManager.UpdateAsync(user);
                return;
            }
            // local user has not been found for the P4M consumer so we need to create one and log them in
            var idResult = await UserManager.CreateAsync(user);
            if (idResult.Succeeded)
            {
                user = await UserManager.FindByEmailAsync(consumer.Email);
                var password = GeneratePassword();
                await UserManager.AddPasswordAsync(user.Id, password);
                await SaveLocalIdAsync(token, user.Id);
            }
        }

        const int passLength = 20;
        const string validChars = "abcdefghijklmnozABCDEFGHIJKLMNOZ1234567890!@#$%^&*()-=";
        Random random = new Random();
        string GeneratePassword()
        {
            StringBuilder strB = new StringBuilder(100);
            int i = 0;
            while (i++ < passLength)
            {
                strB.Append(validChars[random.Next(validChars.Length)]);
            }
            return strB.ToString();
        }

        async Task<bool> LocalLoginAsync(string token, string localId, string email)
        {
            ApplicationUser user = await UserManager.FindByIdAsync(localId);
            if (user == null)
            {
                user = await UserManager.FindByEmailAsync(email);
                if (user == null)
                    return false;
                await SaveLocalIdAsync(token, user.Id);
            }
            var identity = await UserManager.CreateIdentityAsync(user, DefaultAuthenticationTypes.ApplicationCookie);
            AuthenticationManager.SignIn(new AuthenticationProperties { IsPersistent = true }, new ClaimsIdentity(identity));
            return true;
        }

        async Task<Consumer> GetConsumerAsync(string token)
        {
            // get the consumer's details from P4M. 
            var client = new HttpClient();
            client.SetBearerToken(token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var result = await client.GetAsync(P4MConstants.BaseApiAddress + "consumer");
            var messageString = await result.Content.ReadAsStringAsync();
            var message = JsonConvert.DeserializeObject<ConsumerMessage>(messageString);
            if (message.Consumer == null || !message.Success)
                throw new Exception(message.Error);
            return message.Consumer;
        }

        async Task SaveLocalIdAsync(string token, string id)
        {
            // get the consumer's details from P4M. 
            var client = new HttpClient();
            client.SetBearerToken(token);
            string json = "{\"LocalId\":\""+id+"\"}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var result = await client.PostAsync(P4MConstants.BaseApiAddress + "consumerExtras", content);
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
                ValidAudience = P4MConstants.ClientId,
                ValidIssuers = new List<string> { P4MConstants.BaseAddress, "https://parcelfor.me" },
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