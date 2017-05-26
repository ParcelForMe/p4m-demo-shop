namespace OpenOrderFramework.Models
{
    public enum CheckoutMode { Shared, Exclusive }
    public class P4MConsts
    {
        public P4MConsts()
        {
            AppMode = System.Configuration.ConfigurationManager.AppSettings["appMode"];
            if (AppMode == "local")
            {
                BaseIdSrvUrl = "http://localhost:8089";
                BaseApiAddress = "http://localhost:8088/api/v2/";
            }
            else
            {
                BaseIdSrvUrl = $"https://{AppMode}-ids.parcelfor.me";
                BaseApiAddress = $"https://{AppMode}-api.parcelfor.me/api/v2/";
            }
            //P4MUrl = $"https://{AppMode}.parcelfor.me";
            //BaseIdSrvUrl = $"{P4MUrl}:44333";
            //BaseApiAddress = $"{P4MUrl}:44321/api/v2/";
            BaseIdSrvUiUrl = $"{BaseIdSrvUrl}/ui/";
            AuthBaseUrl = $"{BaseIdSrvUrl}/connect/authorize";
            TokenEndpoint = $"{BaseIdSrvUrl}/connect/token";
            LogoutUrl = $"{BaseIdSrvUrl}/connect/endsession";
            JwksUrl = $"{BaseIdSrvUrl}/.well-known/openid-configuration/jwks";

            RedirectUrl = System.Configuration.ConfigurationManager.AppSettings["redirectUrl"];
            LogoutForm = "logoutForm";

            ClientId = System.Configuration.ConfigurationManager.AppSettings["clientId"];
            ClientSecret = System.Configuration.ConfigurationManager.AppSettings["clientSecret"];
        }

        public static string DefaultInitialPostCode { get; set; } = "W1D 1LL";
        public static string DefaultInitialCountryCode { get; set; } = "GB";
        public static CheckoutMode CheckoutMode { get; set; } = CheckoutMode.Shared;
        public string AuthBaseUrl { get; set; }
        public string AppMode { get; set; }
        private string P4MUrl { get; set; }
        public string BaseIdSrvUrl { get; set; }
        public string BaseApiAddress { get; set; }
        public string BaseIdSrvUiUrl { get; set; }
        public string TokenEndpoint { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RedirectUrl { get; set; }  
        public string LogoutForm { get; set; } 
        public string LogoutUrl { get; set; }
        public string JwksUrl { get; set; }
        public string SigningCert { get; set; }
        public static string GfsClientId { get; set; } = "parcel_4_me";
        public static string GfsClientSecret { get; set; } = "needmoreparcels";
    }
}
