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

    public class ConsumerStatusMessage : P4MBaseMessage
    {
        public string UserId { get; set; }
        public bool IsKnown { get; set; }
        public bool IsConfirmed { get; set; }
    }

    public class ConsumerIdMessage : P4MBaseMessage
    {
        public string ConsumerId { get; set; }
    }

    public class ConsumerAndCartMessage
    {
        public Consumer Consumer { get; set; }
        public P4MCart Cart { get; set; }
    }

    public class CartMessage : P4MBaseMessage
    {
        public P4MCart Cart { get; set; }
        public P4MAddress BillTo { get; set; }
        public P4MAddress DeliverTo { get; set; }
    }

    public class CartTotalsMessage : P4MBaseMessage
    {
        public decimal Tax { get; set; }
        public decimal Shipping { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
    }

    public class TokenMessage : P4MBaseMessage
    {
        public string Token { get; set; }
    }

    public class CartUpdateMessage : CartTotalsMessage
    {
        public List<P4MDiscount> Discounts { get; set; }
    }

    public class DiscountMessage : CartUpdateMessage
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class PurchaseResultMessage : P4MBaseMessage
    {
        public string Id { get; set; }
        public string TransactionTypeCode { get; set; }
        public string AuthCode { get; set; }
        public P4MCart Cart { get; set; }
        public P4MAddress DeliverTo { get; set; }
        public P4MAddress BillTo { get; set; }
        public string RedirectUrl { get; set; }
        // 3D Secure fields
        public string ACSUrl { get; set; }
        public string PaReq { get; set; }
        public string ACSResponseUrl { get; set; }
        public string P4MData { get; set; }
    }

    public class PostPurchaseMessage : P4MBaseMessage
    {
        public string  CartId { get; set; }
        public string CVV { get; set; }
        public P4MAddress NewDropPoint { get; set; }
    }
}
