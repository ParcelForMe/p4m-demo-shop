using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace OpenOrderFramework.Controllers
{
    public static class Helpers
    {
        public static DateTime GetTokenExpiry(string token)
        {
            // token = header.payload.signature as Base64 JSON
            var parts = token.Split('.');
            var payload = Base64Decode(parts[1]);
            var jwt = JsonConvert.DeserializeObject<JWT>(payload);
            var offset = DateTimeOffset.FromUnixTimeSeconds(jwt.exp);
            return offset.DateTime.ToLocalTime();
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            if (base64EncodedData.Length % 4 != 0)
                base64EncodedData = base64EncodedData.PadRight(base64EncodedData.Length + (4 - (base64EncodedData.Length % 4)), '=');
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }

    public class JWT
    {
        public int nbf { get; set; }
        public int exp { get; set; }
        public string iss { get; set; }
        public List<string> aud { get; set; }
        public string sub { get; set; }
        public int auth_time { get; set; }
        public string idp { get; set; }
        public string name { get; set; }
        public string role { get; set; }
        public string given_name { get; set; }
        public string family_name { get; set; }
        public string email { get; set; }
        public string consumer_locale { get; set; }
        public string gsid { get; set; }
        public string provs { get; set; }
        public string client_id { get; set; }
        public string client_name { get; set; }
        public string p4m_session_id { get; set; }
        public List<string> scope { get; set; }
        public List<string> amr { get; set; }
    }
}