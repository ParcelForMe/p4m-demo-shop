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

        public void CalcTax(Discount discount = null, decimal taxPercent = 20M)
        {
            var itemsTotal = Items.Sum(d => d.Count * d.Item.Price);
            if (discount != null)
                this.Discount = Math.Round((itemsTotal + this.Shipping) * (discount.Percentage / 100), 2);
            this.Tax = Math.Round((itemsTotal + this.Shipping - this.Discount) * (taxPercent / 100), 2);
            this.Total = itemsTotal + this.Shipping + this.Tax - this.Discount;
            storeDB.SaveChanges();
        }

        public async Task SetItemQtyAsync(int itemId, int qty)
        {
            var cartItem = storeDB.Carts.SingleOrDefault(c => c.CartId == ShoppingCartId && c.ItemId == itemId);
            if (qty <= 0)
                storeDB.Carts.Remove(cartItem);
            else
                cartItem.Count = qty;
            await storeDB.SaveChangesAsync();
            CalcTax();
        }

        public int AddToCart(Item item)
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
                    Count = 1,
                    DateCreated = DateTime.Now
                };
                storeDB.Carts.Add(cartItem);
            }
            else
            {
                // If the item does exist in the cart, 
                // then add one to the quantity
                cartItem.Count++;
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
            var cartItems = storeDB.Carts.Where(
                cart => cart.CartId == ShoppingCartId);

            foreach (var cartItem in cartItems)
            {
                storeDB.Carts.Remove(cartItem);
            }
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
            // Multiply item price by count of that item to get 
            // the current price for each of those items in the cart
            // sum all item price totals to get the cart total
            decimal? total = (from cartItems in storeDB.Carts
                              where cartItems.CartId == ShoppingCartId
                              select (int?)cartItems.Count *
                              cartItems.Item.Price).Sum();

            return total ?? decimal.Zero;
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
                //if (!string.IsNullOrWhiteSpace(context.User.Identity.Name))
                //{
                //    context.Session[CartSessionKey] = context.User.Identity.Name;
                //}
                //else
                //{
                    // Generate a new random GUID using System.Guid class
                    Guid tempCartId = Guid.NewGuid();
                    // Send tempCartId back to client as a cookie
                    context.Session[CartSessionKey] = tempCartId.ToString();
                //}
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