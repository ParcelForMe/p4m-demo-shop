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

namespace OpenOrderFramework.Controllers
{
    public static class P4MConstants
    {
        public const string ClientId = "p4m-login-test";
        public const string ClientSecret = "123456";
        public const string CredClientId = "p4m-login-test";
        public const string CredClientSecret = "123456";
        public const string ClientGuestId = "codeclient_guest";

        //public const string BaseAddress = "https://localhost:44333/core";
        //public const string BaseApiAddress = "https://localhost:44321/api/v1/";
        //public const string BaseAddress = "https://localhost:44333/core";
        //public const string BaseApiAddress = "https://localhost:44321/api/v1/";
        public const string BaseAddress = "https://id.parcelfor.me:44333/core";
        public const string BaseApiAddress = "https://id.parcelfor.me:44321/api/v1/";
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
                return View("ReturnToken");
            }
            return View("error");
        }

        bool ValidateToken(string token, string nonce)
        {
            // this x509 string is taken from https://id.parcelfor.me:44333/core/.well-known/jwks "x5c" - if using a different server then might need to get a different cert from that server
            var x509Str = "MIIFKTCCBBGgAwIBAgIQBpXgM4drGUyWvFilY6mQ/DANBgkqhkiG9w0BAQsFADBNMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMScwJQYDVQQDEx5EaWdpQ2VydCBTSEEyIFNlY3VyZSBTZXJ2ZXIgQ0EwHhcNMTUwMzMwMDAwMDAwWhcNMTYwNDA2MTIwMDAwWjB3MQswCQYDVQQGEwJHQjEUMBIGA1UECBMLV2VzdCBTdXNzZXgxEDAOBgNVBAcTB0NyYXdsZXkxGjAYBgNVBAoTEVBhcmNlbCBGb3IgTWUgTHRkMQswCQYDVQQLEwJJVDEXMBUGA1UEAwwOKi5wYXJjZWxmb3IubWUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDuGud1W2It2TficmFGyflPx59t1zWOU0gjAKh118kV0TnrWM5V1dApFaRwIrnehHhOvQnAsACYV0K3T5ZlMPw3wWv8XF56SGrZHJx3SPlZEIl9UP2J4yU82Fyz8YIIzbdlEFJnQ/5bNkX/qmcL6oFjpkxalHiW04OZwh0fe7XnpvYmRiMP2x1Mss35cArTGagJA8jy2dLIkIp4x0fCng3pPzlNLlIEN1q2ERnuOfY26jJgh5AMoy8POMX2LrcH3HWI106yW4wlYqLGVyVAovx4+D4VJgqB/7Bg1uPKIG12hwMkX/sva/QLbdy3vmptHOn0l+RY4WOlveOVFKfPljnjAgMBAAGjggHZMIIB1TAfBgNVHSMEGDAWgBQPgGEcgjFh1S8o541GOLQs4cbZ4jAdBgNVHQ4EFgQUbM31cW4e631fz227xlUJwZE2UFYwJwYDVR0RBCAwHoIOKi5wYXJjZWxmb3IubWWCDHBhcmNlbGZvci5tZTAOBgNVHQ8BAf8EBAMCBaAwHQYDVR0lBBYwFAYIKwYBBQUHAwEGCCsGAQUFBwMCMGsGA1UdHwRkMGIwL6AtoCuGKWh0dHA6Ly9jcmwzLmRpZ2ljZXJ0LmNvbS9zc2NhLXNoYTItZzMuY3JsMC+gLaArhilodHRwOi8vY3JsNC5kaWdpY2VydC5jb20vc3NjYS1zaGEyLWczLmNybDBCBgNVHSAEOzA5MDcGCWCGSAGG/WwBATAqMCgGCCsGAQUFBwIBFhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMHwGCCsGAQUFBwEBBHAwbjAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEYGCCsGAQUFBzAChjpodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRTSEEyU2VjdXJlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBALDPzKumlhai9D/EJxbwiHxfsZLDxGuoUXNp5gnL2TlE6e0S4HT9wGtTbfL4G+lp6ppxvJN+1ojKC31MuZro7z7s+k1ZZtQVGKucRxN95TQBUmv3gRu4C1zWFYoxCd8k1VZ77sKSOngQ7V7c9EIakA9Q5zfNo6vAcQzff0mMRfTHCNrZV5t9WQxi2HBLu3OLt98QZ8bJ31oGRR6pmAkKnhHzKc6qd8THn2WfEt5rFBRQ5ts785ZU+kOAMvVv0i1cwmMJYo9nySmkXd4EP3YZcHxSvESSBFwOhpVr6Gez+33nAR8hYj10Upv8SHtLYxouqz1EItDj/iXmLhSD4HLdkqg=";
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