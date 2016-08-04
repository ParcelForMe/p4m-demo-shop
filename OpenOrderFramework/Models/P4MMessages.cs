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
        public bool HasOpenCart { get; set; }
    }

    public class CartMessage : P4MBaseMessage
    {
        public P4MCart Cart { get; set; }
        public P4MAddress BillTo { get; set; }
        public P4MAddress DeliverTo { get; set; }
    }

    public class DiscountMessage : P4MBaseMessage
    {
        public string Code { get; set; }
        public decimal Tax { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class CartUpdateMessage : P4MBaseMessage
    {
        public decimal Tax { get; set; }
        public decimal Shipping { get; set; }
        public decimal Discount { get; set; }
    }

    public class PurchaseMessage : P4MBaseMessage
    {
        public string Id { get; set; }
        public string TransactionTypeCode { get; set; }
        public string AuthCode { get; set; }
        public P4MCart Cart { get; set; }
        public P4MAddress DeliverTo { get; set; }
        public P4MAddress BillTo { get; set; }
        // 3D Secure fields
        public string ACSUrl { get; set; }
        public string PaReq { get; set; }
        public string ACSResponseUrl { get; set; }
        public string P4MData { get; set; }
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
