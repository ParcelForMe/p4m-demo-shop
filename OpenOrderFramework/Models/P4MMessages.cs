using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenOrderFramework.Models
{
    public class P4MBaseMessage
    {
        public bool Success { get { return string.IsNullOrWhiteSpace(Error); } }
        public string Error { get; set; }
    }

    public class LoginMessage : P4MBaseMessage
    {
        public string RedirectUrl { get; set; }
    }

    public class ConsumerMessage : P4MBaseMessage
    {
        public Consumer Consumer { get; set; }
    }

    public class CartMessage : P4MBaseMessage
    {
        public P4MCart Cart { get; set; }
    }

    public class DiscountMessage : P4MBaseMessage
    {
        public decimal Tax { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class CartUpdateMessage : P4MBaseMessage
    {
        public decimal Tax { get; set; }
        public decimal Shipping { get; set; }
    }

    public class PostCartMessage : P4MBaseMessage
    {
        public string  SessionId { get; set; }
        public string PaymentType { get; set; }
        public string Currency { get; set; }
        public string PayMethodToken { get; set; }
        public bool ClearItems { get; set; }
        public P4MCart Cart { get; set; }
    }
}
