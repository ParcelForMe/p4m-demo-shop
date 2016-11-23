namespace OpenOrderFramework.Models
{
    public class P4MUrls
    {
        public P4MUrls()
        {
            AppMode = "dev";
            P4MUrl = $"https://{AppMode}.parcelfor.me";
            BaseIdSrvUrl = $"{P4MUrl}:44333";
            BaseApiAddress = $"{P4MUrl}:44321/api/v2/";
            BaseIdSrvUiUrl = $"{BaseIdSrvUrl}/ui/";
            AuthBaseUrl = $"{BaseIdSrvUrl}/connect/authorize";
            TokenEndpoint = $"{BaseIdSrvUrl}/connect/token";
            LogoutUrl = $"{BaseIdSrvUrl}/connect/endsession";

            RedirectUrl = "http://localhost:3000/p4m/getP4MAccessToken";
            LogoutForm = "logoutForm";

            ClientId = "10004";
            ClientSecret = "secret";
        }

        public string AuthBaseUrl { get; set; }
        public string AppMode { get; set; }
        public string P4MUrl { get; set; }
        public string BaseIdSrvUrl { get; set; }
        public string BaseApiAddress { get; set; }
        public string BaseIdSrvUiUrl { get; set; }
        public string TokenEndpoint { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RedirectUrl { get; set; }  
        public string LogoutForm { get; set; } 
        public string LogoutUrl { get; set; }  
    }
}