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
        public Address PrefDeliveryAddress {
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

        public List<Address> Addresses { get; set; }

        public List<PaymentMethod> PaymentMethods { get; set; }

        public Dictionary<string, string> Extras { get; set; }
    }

    public class Address 
    {
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

    public class PaymentMethod
    {
        public string Id { get; set; }
        public string AccountType { get; set; }
        public string Issuer { get; set; }
        public string Description { get; set; }
        public string MoreDetail { get; set; }
    }

    public class PaymentCard
    {
        public string Id { get; set; } // guid set on app/web
        public string NameOnCard { get; set; }    // name on card 
        public string BrandId { get; set; }   // payment company e.g. VISA, etc
        public string Last4Digits { get; set; }
        public int ExpiryMonth { get; set; }
        public int ExpiryYear { get; set; }
    }
}
