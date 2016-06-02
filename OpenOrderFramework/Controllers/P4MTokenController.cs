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

namespace OpenOrderFramework.Controllers
{
    public static class P4MConstants
    {
        public const string ClientId = "codeclient";
        public const string ClientSecret = "secret";
        public const string CredClientId = "p4mNeilClientCC";
        public const string ClientGuestId = "codeclient_guest";

        //public const string BaseAddress = "https://localhost:44333/core";
        //public const string BaseApiAddress = "https://localhost:44321/api/v1/";
        public const string BaseAddress = "https://dev.parcelfor.me:44333/core";
        public const string BaseApiAddress = "https://dev.parcelfor.me:44321/api/v1/";
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
            var response = await client.RequestAuthorizationCodeAsync(code, P4MConstants.LocalCallbackUrl);
            if (ValidateToken(response.IdentityToken, nonceFromCookie) && !string.IsNullOrEmpty(response.AccessToken))
            {
                //var parsedToken = ParseJwt(response.AccessToken);
                this.Response.Cookies["p4mToken"].Value = response.AccessToken;
                return View("ReturnToken");
            }
            return View("error");
        }

        bool ValidateToken(string token, string nonce)
        {
            //var certString = "MIIDBTCCAfGgAwIBAgIQNQb+T2ncIrNA6cKvUA1GWTAJBgUrDgMCHQUAMBIxEDAOBgNVBAMTB0RldlJvb3QwHhcNMTAwMTIwMjIwMDAwWhcNMjAwMTIwMjIwMDAwWjAVMRMwEQYDVQQDEwppZHNydjN0ZXN0MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqnTksBdxOiOlsmRNd+mMS2M3o1IDpK4uAr0T4/YqO3zYHAGAWTwsq4ms+NWynqY5HaB4EThNxuq2GWC5JKpO1YirOrwS97B5x9LJyHXPsdJcSikEI9BxOkl6WLQ0UzPxHdYTLpR4/O+0ILAlXw8NU4+jB4AP8Sn9YGYJ5w0fLw5YmWioXeWvocz1wHrZdJPxS8XnqHXwMUozVzQj+x6daOv5FmrHU1r9/bbp0a1GLv4BbTtSh4kMyz1hXylho0EvPg5p9YIKStbNAW9eNWvv5R8HN7PPei21AsUqxekK0oW9jnEdHewckToX7x5zULWKwwZIksll0XnVczVgy7fCFwIDAQABo1wwWjATBgNVHSUEDDAKBggrBgEFBQcDATBDBgNVHQEEPDA6gBDSFgDaV+Q2d2191r6A38tBoRQwEjEQMA4GA1UEAxMHRGV2Um9vdIIQLFk7exPNg41NRNaeNu0I9jAJBgUrDgMCHQUAA4IBAQBUnMSZxY5xosMEW6Mz4WEAjNoNv2QvqNmk23RMZGMgr516ROeWS5D3RlTNyU8FkstNCC4maDM3E0Bi4bbzW3AwrpbluqtcyMN3Pivqdxx+zKWKiORJqqLIvN8CT1fVPxxXb/e9GOdaR8eXSmB0PgNUhM4IjgNkwBbvWC9F/lzvwjlQgciR7d4GfXPYsE1vf8tmdQaY8/PtdAkExmbrb9MihdggSoGXlELrPA91Yce+fiRcKY3rQlNWVd4DOoJ/cPXsXwry8pWjNCo5JD8Q+RQ5yZEy7YPoifwemLhTdsBz3hlZr28oCGJ3kbnpW0xGvQb3VHSTVVbeei0CfXoW6iz1";
            // this x509 string is taken from https://id.parcelfor.me:44333/core/.well-known/jwks "x5c"
            var x509Str = "MIIFKTCCBBGgAwIBAgIQBpXgM4drGUyWvFilY6mQ/DANBgkqhkiG9w0BAQsFADBNMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMScwJQYDVQQDEx5EaWdpQ2VydCBTSEEyIFNlY3VyZSBTZXJ2ZXIgQ0EwHhcNMTUwMzMwMDAwMDAwWhcNMTYwNDA2MTIwMDAwWjB3MQswCQYDVQQGEwJHQjEUMBIGA1UECBMLV2VzdCBTdXNzZXgxEDAOBgNVBAcTB0NyYXdsZXkxGjAYBgNVBAoTEVBhcmNlbCBGb3IgTWUgTHRkMQswCQYDVQQLEwJJVDEXMBUGA1UEAwwOKi5wYXJjZWxmb3IubWUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDuGud1W2It2TficmFGyflPx59t1zWOU0gjAKh118kV0TnrWM5V1dApFaRwIrnehHhOvQnAsACYV0K3T5ZlMPw3wWv8XF56SGrZHJx3SPlZEIl9UP2J4yU82Fyz8YIIzbdlEFJnQ/5bNkX/qmcL6oFjpkxalHiW04OZwh0fe7XnpvYmRiMP2x1Mss35cArTGagJA8jy2dLIkIp4x0fCng3pPzlNLlIEN1q2ERnuOfY26jJgh5AMoy8POMX2LrcH3HWI106yW4wlYqLGVyVAovx4+D4VJgqB/7Bg1uPKIG12hwMkX/sva/QLbdy3vmptHOn0l+RY4WOlveOVFKfPljnjAgMBAAGjggHZMIIB1TAfBgNVHSMEGDAWgBQPgGEcgjFh1S8o541GOLQs4cbZ4jAdBgNVHQ4EFgQUbM31cW4e631fz227xlUJwZE2UFYwJwYDVR0RBCAwHoIOKi5wYXJjZWxmb3IubWWCDHBhcmNlbGZvci5tZTAOBgNVHQ8BAf8EBAMCBaAwHQYDVR0lBBYwFAYIKwYBBQUHAwEGCCsGAQUFBwMCMGsGA1UdHwRkMGIwL6AtoCuGKWh0dHA6Ly9jcmwzLmRpZ2ljZXJ0LmNvbS9zc2NhLXNoYTItZzMuY3JsMC+gLaArhilodHRwOi8vY3JsNC5kaWdpY2VydC5jb20vc3NjYS1zaGEyLWczLmNybDBCBgNVHSAEOzA5MDcGCWCGSAGG/WwBATAqMCgGCCsGAQUFBwIBFhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMHwGCCsGAQUFBwEBBHAwbjAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEYGCCsGAQUFBzAChjpodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRTSEEyU2VjdXJlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBALDPzKumlhai9D/EJxbwiHxfsZLDxGuoUXNp5gnL2TlE6e0S4HT9wGtTbfL4G+lp6ppxvJN+1ojKC31MuZro7z7s+k1ZZtQVGKucRxN95TQBUmv3gRu4C1zWFYoxCd8k1VZ77sKSOngQ7V7c9EIakA9Q5zfNo6vAcQzff0mMRfTHCNrZV5t9WQxi2HBLu3OLt98QZ8bJ31oGRR6pmAkKnhHzKc6qd8THn2WfEt5rFBRQ5ts785ZU+kOAMvVv0i1cwmMJYo9nySmkXd4EP3YZcHxSvESSBFwOhpVr6Gez+33nAR8hYj10Upv8SHtLYxouqz1EItDj/iXmLhSD4HLdkqg=";
            var cert = new X509Certificate2(Convert.FromBase64String(x509Str));

            var parameters = new TokenValidationParameters
            {
                ValidAudience = P4MConstants.ClientId,
                ValidIssuer = P4MConstants.BaseAddress,
                IssuerSigningToken = new X509SecurityToken(cert)
            };

            SecurityToken jwt;
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out jwt);

            // validate nonce
            var nonceClaim = principal.FindFirst("nonce");

            if (!string.Equals(nonceClaim.Value, nonce, StringComparison.Ordinal))
            {
                throw new Exception("invalid nonce");
            }

            return true;
        }

        //private async Task<IEnumerable<Claim>> GetUserInfoClaimsAsync(string accessToken)
        //{
        //    var userInfoClient = new UserInfoClient(new Uri(Constants.UserInfoEndpoint), accessToken);

        //    var userInfo = await userInfoClient.GetAsync();

        //    var claims = new List<Claim>();
        //    userInfo.Claims.ToList().ForEach(ui => claims.Add(new Claim(ui.Item1, ui.Item2)));

        //    return claims;
        //}

        private string ParsedJwt(string token)
        {
            if (!token.Contains("."))
            {
                return token;
            }

            var parts = token.Split('.');
            var part = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));

            var jwt = JObject.Parse(part);
            return jwt.ToString();
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