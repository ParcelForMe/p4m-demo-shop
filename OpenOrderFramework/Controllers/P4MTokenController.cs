using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Thinktecture.IdentityModel.Client;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpenOrderFramework.Controllers
{
    public static class P4MConstants
    {
        public const string ClientId = "codeclient";
        public const string ClientSecret = "secret";
        public const string CredClientId = "p4mNeilClientCC";
        public const string ClientGuestId = "codeclient_guest";

        public const string BaseAddress = "https://localhost:44333/core";
        public const string BaseApiAddress = "https://localhost:44321/api/v1/";
        public const string LocalCallbackUrl = "http://localhost:3000/auth/oidc/callback";

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
        [Route("/getConsumerAccessToken")]
        public async Task<string> GetToken(string code)
//        public async Task<ActionResult> GetToken(string code)
        {
            var client = new OAuth2Client(new Uri(P4MConstants.TokenEndpoint), P4MConstants.ClientId, P4MConstants.ClientSecret);
            var response = await client.RequestAuthorizationCodeAsync(code, P4MConstants.LocalCallbackUrl);
            if (!string.IsNullOrEmpty(response.AccessToken))
            {
                //var parsedToken = ParseJwt(response.AccessToken);
                return response.AccessToken;
                //return View("Token", response);
            }
            return string.Empty;
        }

        private string ParseJwt(string token)
        {
            if (!token.Contains("."))
            {
                return token;
            }

            var parts = token.Split('.');
            byte[] decbuff = HttpServerUtility.UrlTokenDecode(parts[1]);
            var part = Encoding.UTF8.GetString(decbuff);
            
            var jwt = JObject.Parse(part);
            return jwt.ToString();
        }
    }
}