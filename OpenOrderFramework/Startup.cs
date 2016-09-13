using Owin;
using System.Net;

namespace OpenOrderFramework
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // need to allow for servers that don't support just TLS1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            ConfigureAuth(app);
        }
    }
}
