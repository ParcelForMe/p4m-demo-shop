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

    public class ConsumerMessage : P4MBaseMessage
    {
        public Consumer Consumer { get; set; }
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
}
