namespace OpenOrderFramework.Migrations
{
    using System.Linq;
    using System.Security.Claims;
    using Microsoft.AspNet.Identity;
    using Microsoft.AspNet.Identity.EntityFramework;
    using Microsoft.AspNet.Identity.Owin;
    using Microsoft.Owin;
    using Microsoft.Owin.Security;
    using System;
    using System.Collections.Generic;
    using System.Data.Entity;
    using System.Threading.Tasks;
    using System.Web;
    using System.Data.Entity.Migrations;
    using OpenOrderFramework.Models;

    internal sealed class Configuration : DbMigrationsConfiguration<OpenOrderFramework.Models.ApplicationDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(OpenOrderFramework.Models.ApplicationDbContext context)
        {
            //  This method will be called after migrating to the latest version.

            //  Within the Package Manager Console run Update-Database to execute any migration and this Seed function

            var roleStore = new RoleStore<IdentityRole>(context);
            var roleManager = new RoleManager<IdentityRole>(roleStore);
            var userStore = new UserStore<ApplicationUser>(context);
            var userManager = new UserManager<ApplicationUser>(userStore);
            var user = new ApplicationUser { UserName = "admin@gmail.com" };
            var guestUser = new ApplicationUser { UserName = "guest@guest.com" };
            // Only  add ne users if this is the first run of the program and there are no users! 
            if  (roleManager.FindByName("Admin") == null)
                {

                    userManager.Create(user, "abc123"); //strong password!#@$!
                    userManager.Create(guestUser, "guest1"); //strong password!#@$!

                    roleManager.Create(new IdentityRole { Name = "Admin" });
                    userManager.AddToRole(user.Id, "Admin");

                 }
           
            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data. E.g.
            //
            var seedData = new List<Catagorie>
            {
                new Catagorie
                {
                    Name = "Womens Clothing",
                    Items = new List<Item>
                    {
                        new Item { Name = "Green Dress", Price = 45.99m, ItemPictureUrl = "/images/wsd008a_2.jpg" },
                        new Item { Name = "Halter Top", Price = 26.99m, ItemPictureUrl = "/images/wbk002a.jpg" },
                        new Item { Name = "Party Dress", Price = 96.99m, ItemPictureUrl = "/images/wds003.jpg" },
                        new Item { Name = "Smart Cream Top", Price = 33.99m, ItemPictureUrl = "/images/wbk006a.jpg" }
                    }
                },
                new Catagorie
                {
                    Name = "Mens Clothing",
                    Items = new List<Item>
                    {
                        new Item { Name = "Blue Shirt", Price = 45.99m, ItemPictureUrl = "/images/msj003a_2.jpg" },
                        new Item { Name = "Casual Trousers", Price = 26.99m, ItemPictureUrl = "/images/mpd000a_3.jpg" },
                        new Item { Name = "Smart Trousers", Price = 96.99m, ItemPictureUrl = "/images/mpd012a_2.jpg" }      
                    }
                },
                 new Catagorie
                {
                    Name = "Mens Shoes",
                    Items = new List<Item>
                    {
                        new Item { Name = "Blue Trainers", Price = 28.99m, ItemPictureUrl = "/images/msh010.jpg" },
                        new Item { Name = "Smart Brown loafers", Price = 26.99m, ItemPictureUrl = "/images/msh007_1.jpg" }
                       
                    }
                },
                  new Catagorie
                {
                    Name = "Womens Shoes",
                    Items = new List<Item>
                    {
                        new Item { Name = "Blue Party Shoes", Price = 45.99m, ItemPictureUrl = "/images/wsh000_1.jpg" },
                        new Item { Name = "Saprkly Party", Price = 66.99m, ItemPictureUrl = "/images/wsh003_3_1.jpg" },
                        new Item { Name = "Comfy Shoes", Price = 96.99m, ItemPictureUrl = "/images/wsh007_3.jpg" }
                    }
                }

            };

            seedData.ForEach(a => context.Catagories.AddOrUpdate(x => x.Name, a));

            var discounts = new List<Discount>
            {
                new Discount
                {
                    Code = "DISCOUNT1",
                    Description = "10% discount",
                    Percentage = 10m
                },
                new Discount
                {
                    Code = "DISCOUNT2",
                    Description = "6% discount",
                    Percentage = 6m
                },
            };

            discounts.ForEach(a => context.Discounts.AddOrUpdate(x => x.Code, a));
        }
    }
}
