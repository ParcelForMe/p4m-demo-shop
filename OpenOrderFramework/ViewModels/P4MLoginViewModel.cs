namespace OpenOrderFramework.ViewModels
{
    public class P4MLoginViewModel
    {
        public P4MLoginViewModel()
        {
            AuthBaseUrl = "https://local.parcelfor.me:44333/connect/authorize";
            ClientId = "10004";
            RedirectUrl = "http://localhost:3000/p4m/getP4MAccessToken";
            LogoutForm = "logoutForm";
            LogoutUrl = "https://local.parcelfor.me:44333/connect/endsession";
        }

        public string AuthBaseUrl { get; set; }  
        public string ClientId { get; set; } 
        public string RedirectUrl { get; set; }  
        public string LogoutForm { get; set; } 
        public string LogoutUrl { get; set; }  
    }
}