using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenOrderFramework.Models
{
    public class Consumer
    {
        public string Id { get; set; }
        public string Locale { get; set; }
        public string Salutation { get; set; }
        public string GivenName { get; set; }
        public string MiddleName { get; set; }
        public string FamilyName { get; set; }
        public string Email { get; set; }
        public string MobilePhone { get; set; }
        public string PreferredCurrency { get; set; }
        public string Language { get; set; }    // e.g. "en", "fr", "de", etc
        public DateTime? DOB { get; set; }
        public bool HasDemoData { get; set; }

        public string Gender { get; set; }
        public string Height { get; set; }
        public string Weight { get; set; }
        public string Waist { get; set; }


        public List<string> PreferredCarriers { get; set; }

        public string PrefDeliveryAddressId { get; set; }
        public P4MAddress PrefDeliveryAddress {
            get {
                if (this.Addresses != null && PrefDeliveryAddressId != null)
                    return Addresses.FirstOrDefault(a => a.Id == PrefDeliveryAddressId);
                return null;
            }
        }
        public string BillingAddressId { get; set; }
        public string DefaultPayMethodToken { get; set; }
        public string ProfilePicHash { get; set; }
        public string DeliveryPreferences { get; set; }

        public string ProfilePicUrl { get; set; }

        public List<P4MAddress> Addresses { get; set; }

        public List<PaymentMethod> PaymentMethods { get; set; }

        public Dictionary<string, string> Extras { get; set; }
    }

    public class PaymentMethod
    {
        public string Id { get; set; }
        public string AccountType { get; set; }
        public string Issuer { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MoreDetail { get; set; }
    }

    //public class PaymentCard
    //{
    //    public string Id { get; set; } // guid set on app/web
    //    public string NameOnCard { get; set; }    // name on card 
    //    public string BrandId { get; set; }   // payment company e.g. VISA, etc
    //    public string Last4Digits { get; set; }
    //    public int ExpiryMonth { get; set; }
    //    public int ExpiryYear { get; set; }
    //}

    public class P4MCart
    {
        public string Id { get; set; }
        public string Reference { get; set; }
        public string SessionId { get; set; }
        public string AddressId { get; set; }
        public string BillingAddressId { get; set; }
        public DateTime? Date { get; set; }
        public string Currency { get; set; }
        public double ShippingAmt { get; set; }
        public double Tax { get; set; }
        public double Total { get; set; }
        public string ServiceLevel { get; set; }
        public DateTime? ExpDeliveryDate { get; set; }
        public string CarrierToken { get; set; }
        public string Carrier { get; set; }
        public string ConsignmentId { get; set; }
        public string PaymentType { get; set; }     // "DB" or "PA"
        public string PayMethodId { get; set; }
        public string PaymentId { get; set; }
        public List<P4MCartItem> Items { get; set; }
        public List<P4MDiscount> Discounts { get; set; }
    }

    public class P4MCartItem
    {
        public string LineId { get; set; }
        public string Make { get; set; }
        public string Sku { get; set; }
        public string Desc { get; set; }
        public double Qty { get; set; }
        public double Price { get; set; }
        public double Discount { get; set; }
        public string LinkToImage { get; set; }
        public string LinkToItem { get; set; }
        public string Tags { get; set; }
        public int Rating { get; set; }
        public string SiteReference { get; set; }   // retailer defined e.g. page code where consumer selected item
        // item options - stores the options selected by the consumer when this item was purchased
        public Dictionary<string, string> Options { get; set; }
    }

    public class P4MAddress 
    {
        public string ConsumerId { get; set; }
        public string Id { get; set; } // guid
        public string AddressType { get; set; }
        public string Label { get; set; }
        public string CompanyName { get; set; }
        public string Street1 { get; set; }
        public string Street2 { get; set; }
        public string City { get; set; }
        public string PostCode { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string Contact { get; set; } // name of the person to contact at the address
        public string Phone { get; set; }   // phone number of the contact or landline at the address
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int DropPointProviderId { get; set; }
        public string DropPointId { get; set; }
        public int CollectPrefOrder { get; set; }
    }

    public class P4MDiscount
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public double Amount { get; set; }
    }

    public class ChangedItem
    {
        public string ItemCode { get; set; }
        public decimal Qty { get; set; }
    }

    public class P4MRedirect
    {
        public string RedirectUrl { get; set; }
    }
}
