using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace OpenOrderFramework.Models
{
    public partial class ShoppingCart
    {
        static ApplicationDbContext storeDB = new ApplicationDbContext();

        public const string CartSessionKey = "CartId";
        public const string OrderSessionKey = "OrderId";

        // Helper method to simplify shopping cart calls
        public static ShoppingCart GetCart(Controller controller)
        {
            return GetCart(controller.HttpContext);
        }

        public static ShoppingCart GetCart(HttpContextBase context)
        {
            var shoppingCartId = GetCartId(context);
            var cart = storeDB.ShoppingCarts.Find(shoppingCartId);
            if (cart == null)
            {
                cart = new ShoppingCart { ShoppingCartId = shoppingCartId };
                storeDB.ShoppingCarts.Add(cart);
            }
            cart.CalcTax();
            return cart;
        }

        public void CalcTax(decimal taxPercent = 20M)
        {
            decimal itemsTotal = Items == null ? 0 : Items.Sum(d => d.Count * d.Item.Price);
            this.Discount = 0;
            foreach(var discount in this.Discounts)
            {
                discount.Amount = Math.Round(itemsTotal * (discount.Discount.Percentage / 100), 2);
                this.Discount += discount.Amount;
            }
            this.Tax = Math.Round((itemsTotal + this.Shipping - this.Discount) * (taxPercent / 100), 2);
            this.Total = itemsTotal + this.Shipping + this.Tax - this.Discount;
            storeDB.SaveChanges();
        }

        public async Task SetItemQtyAsync(int itemId, int qty)
        {
            var cartItem = storeDB.Carts.SingleOrDefault(c => c.CartId == ShoppingCartId && c.ItemId == itemId);
            if (qty <= 0)
            {
                if (cartItem != null)
                    storeDB.Carts.Remove(cartItem);
            }
            else
            {
                if (cartItem == null)
                    storeDB.Carts.Add(new Cart { CartId = ShoppingCartId, ItemId = itemId, Count = qty, DateCreated = DateTime.Now });
                else
                    cartItem.Count = qty;
            }
            await storeDB.SaveChangesAsync();
            CalcTax();
        }

        public int AddToCart(Item item, int count = -1)
        {
            // Get the matching cart and item instances
            var cartItem = storeDB.Carts.SingleOrDefault(c => c.CartId == ShoppingCartId && c.ItemId == item.ID);
            if (cartItem == null)
            {
                // Create a new cart item if no cart item exists
                cartItem = new Cart
                {
                    ItemId = item.ID,
                    CartId = ShoppingCartId,
                    Count = count > 0 ? count : 1,
                    DateCreated = DateTime.Now
                };
                storeDB.Carts.Add(cartItem);
            }
            else
            {
                // If the item does exist in the cart, 
                // then add one to the quantity
                cartItem.Count = count > 0 ? count : cartItem.Count + 1;
            }
            // Save changes
            storeDB.SaveChanges();
            return cartItem.Count;
        }

        public int RemoveFromCart(int id)
        {
            // Get the cart
            var cartItem = storeDB.Carts.Single(cart => cart.CartId == ShoppingCartId && cart.ItemId == id);
            int itemCount = 0;
            if (cartItem != null)
            {
                if (cartItem.Count > 1)
                {
                    cartItem.Count--;
                    itemCount = cartItem.Count;
                }
                else
                {
                    storeDB.Carts.Remove(cartItem);
                }
                // Save changes
                storeDB.SaveChanges();
            }
            return itemCount;
        }

        public void EmptyCart()
        {
            var cartItems = storeDB.Carts.Where(cart => cart.CartId == ShoppingCartId);
            foreach (var cartItem in cartItems)
                storeDB.Carts.Remove(cartItem);
            var cartDiscs = storeDB.CartDiscounts.Where(disc => disc.CartId == ShoppingCartId);
            foreach (var disc in cartDiscs)
                storeDB.CartDiscounts.Remove(disc);
            storeDB.ShoppingCarts.Remove(this);
            // Save changes
            storeDB.SaveChanges();
        }

        [NotMapped]
        public List<Cart> Items
        {
            get { return GetCartItems(); }
        }

        public List<Cart> GetCartItems()
        {
            var result = storeDB.Carts.Where(cart => cart.CartId == ShoppingCartId).ToList();
            foreach (var item in result)
                item.Item = storeDB.Items.Find(item.ItemId);
            return result;
        }

        [NotMapped]
        public List<CartDiscount> Discounts
        {
            get { return GetCartDiscounts(); }
        }

        public List<CartDiscount> GetCartDiscounts()
        {
            var result = storeDB.CartDiscounts.Where(disc => disc.CartId == ShoppingCartId).ToList();
            foreach (var disc in result)
                disc.Discount = storeDB.Discounts.Where(d => d.Code == disc.DiscountCode).FirstOrDefault();
            return result;
        }

        public int GetCount()
        {
            // Get the count of each item in the cart and sum them up
            int? count = (from cartItems in storeDB.Carts
                          where cartItems.CartId == ShoppingCartId
                          select (int?)cartItems.Count).Sum();
            // Return 0 if all entries are null
            return count ?? 0;
        }

        public decimal GetTotal()
        {
            CalcTax();
            return Total;
        }

        public Order CreateOrder(Order order)
        {
            decimal orderTotal = 0;
            order.OrderDetails = new List<OrderDetail>();

            var cartItems = GetCartItems();
            // Iterate over the items in the cart, 
            // adding the order details for each
            foreach (var item in cartItems)
            {
                var orderDetail = new OrderDetail
                {
                    ItemId = item.ItemId,
                    OrderId = order.OrderId,
                    UnitPrice = item.Item.Price,
                    Quantity = item.Count
                };
                // Set the order total of the shopping cart
                orderTotal += (item.Count * item.Item.Price);
                order.OrderDetails.Add(orderDetail);
                storeDB.OrderDetails.Add(orderDetail);

            }
            // Set the order's total to the orderTotal count
            order.Total = orderTotal;

            // Save the order
            storeDB.SaveChanges();
            // Empty the shopping cart
            EmptyCart();
            // Return the OrderId as the confirmation number
            return order;
        }

        // We're using HttpContextBase to allow access to cookies.
        public static string GetCartId(HttpContextBase context)
        {
            if (context.Session[CartSessionKey] == null)
            {
                // Generate a new random GUID using System.Guid class
                Guid tempCartId = Guid.NewGuid();
                // Send tempCartId back to client as a cookie
                context.Session[CartSessionKey] = tempCartId.ToString();
            }
            return context.Session[CartSessionKey].ToString();
        }

        // When a user has logged in, migrate their shopping cart to
        // be associated with their username
        public void MigrateCart(string userName)
        {
            var shoppingCart = storeDB.Carts.Where(
                c => c.CartId == ShoppingCartId);

            foreach (Cart item in shoppingCart)
            {
                item.CartId = userName;
            }
            storeDB.SaveChanges();
        }
    }
}