using System.ComponentModel.DataAnnotations;

namespace OpenOrderFramework.Models
{
    public class Cart
    {
        [Key]
        public int ID { get; set; }
        public string CartId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public System.DateTime DateCreated { get; set; }
        public virtual Item Item { get; set; }
    }

    public partial class ShoppingCart
    {
        [Key]
        public string ShoppingCartId { get; set; }
        public decimal Tax { get; set; }
        public decimal Shipping { get; set; }
        public string DiscountCode { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
    }
}