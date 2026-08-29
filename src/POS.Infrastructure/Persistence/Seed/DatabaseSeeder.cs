using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Application.Common;
using POS.Application.Common.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.ValueObjects;

namespace POS.Infrastructure.Persistence.Seed;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext context, IPasswordHasher passwordHasher, ILogger logger)
    {
        try
        {
            if (context.Database.IsRelational())
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

            // 1. Seed Permissions
            var existingPermissions = await context.Permissions.Select(p => p.Code).ToListAsync();
            foreach (var permCode in Permissions.All)
            {
                if (!existingPermissions.Contains(permCode))
                {
                    context.Permissions.Add(new Permission
                    {
                        Code = permCode,
                        Name = permCode.Replace("Permissions.", "").Replace(".", " "),
                        Category = permCode.Split('.')[1]
                    });
                }
            }
            await context.SaveChangesAsync();

            // 2. Seed Roles
            var allPermissions = await context.Permissions.ToListAsync();
            var superAdminRole = await context.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Name == Roles.SuperAdmin);
            if (superAdminRole == null)
            {
                superAdminRole = new Role
                {
                    Name = Roles.SuperAdmin,
                    Description = "Full access to all system features",
                    IsSystemRole = true
                };
                foreach (var perm in allPermissions)
                {
                    superAdminRole.RolePermissions.Add(new RolePermission { RoleId = superAdminRole.Id, PermissionId = perm.Id });
                }
                context.Roles.Add(superAdminRole);
            }

            var cashierRole = await context.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Name == Roles.Cashier);
            if (cashierRole == null)
            {
                cashierRole = new Role
                {
                    Name = Roles.Cashier,
                    Description = "POS cashier operations",
                    IsSystemRole = true
                };
                var cashierPerms = allPermissions.Where(p => p.Code == Permissions.ProcessSales || p.Code == Permissions.ViewSales || p.Code == Permissions.SynchronizeData || p.Code == Permissions.ManageCustomers);
                foreach (var perm in cashierPerms)
                {
                    cashierRole.RolePermissions.Add(new RolePermission { RoleId = cashierRole.Id, PermissionId = perm.Id });
                }
                context.Roles.Add(cashierRole);
            }

            await context.SaveChangesAsync();

            // 3. Seed Default Store
            var defaultStore = await context.Stores.FirstOrDefaultAsync(s => s.Code == "STORE-001");
            if (defaultStore == null)
            {
                defaultStore = new Store
                {
                    Code = "STORE-001",
                    Name = "Flagship Store",
                    Description = "Primary Retail Location",
                    Address = new Address("100 Market St", "Metropolis", "NY", "10001", "USA"),
                    CurrencyCode = "USD",
                    IsActive = true
                };
                context.Stores.Add(defaultStore);
                await context.SaveChangesAsync();
            }

            // 4. Seed Default Terminal
            var defaultTerminal = await context.PosTerminals.FirstOrDefaultAsync(t => t.TerminalCode == "TERM-01" && t.StoreId == defaultStore.Id);
            if (defaultTerminal == null)
            {
                defaultTerminal = new PosTerminal
                {
                    StoreId = defaultStore.Id,
                    TerminalCode = "TERM-01",
                    TerminalName = "Front Register 1",
                    IsActive = true
                };
                context.PosTerminals.Add(defaultTerminal);
                await context.SaveChangesAsync();
            }

            // 5. Seed SuperAdmin User
            var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            if (adminUser == null)
            {
                adminUser = new User
                {
                    StoreId = defaultStore.Id,
                    Username = "admin",
                    Email = "admin@pos-system.local",
                    PasswordHash = passwordHasher.HashPassword("Admin@123456"),
                    FullName = "System Administrator",
                    Status = UserStatus.Active
                };
                adminUser.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = superAdminRole.Id });
                context.Users.Add(adminUser);
                await context.SaveChangesAsync();
            }

            // 6. Seed Demo Categories and Products if empty
            if (!await context.Categories.AnyAsync())
            {
                var catBeverages = new Category { Name = "Beverages", Description = "Drinks and refreshments" };
                var catBakery = new Category { Name = "Bakery", Description = "Freshly baked goods" };
                var catGroceries = new Category { Name = "Groceries", Description = "Pantry and general essentials" };

                context.Categories.AddRange(catBeverages, catBakery, catGroceries);
                await context.SaveChangesAsync();

                var p1 = new Product
                {
                    StoreId = defaultStore.Id,
                    CategoryId = catBeverages.Id,
                    Sku = "BEV-001",
                    Name = "Espresso Dark Roast 250g",
                    CostPrice = 3.50m,
                    RetailPrice = 7.99m,
                    TaxRate = 0.08m,
                    LowStockThreshold = 10,
                    TrackInventory = true
                };
                p1.Barcodes.Add(new ProductBarcode { ProductId = p1.Id, Barcode = "793573192011", IsPrimary = true });

                var p2 = new Product
                {
                    StoreId = defaultStore.Id,
                    CategoryId = catBakery.Id,
                    Sku = "BAK-001",
                    Name = "Artisan Sourdough Loaf",
                    CostPrice = 1.20m,
                    RetailPrice = 4.50m,
                    TaxRate = 0.00m,
                    LowStockThreshold = 5,
                    TrackInventory = true
                };
                p2.Barcodes.Add(new ProductBarcode { ProductId = p2.Id, Barcode = "793573192028", IsPrimary = true });

                context.Products.AddRange(p1, p2);
                await context.SaveChangesAsync();

                // Initial Stock
                var s1 = new Stock { StoreId = defaultStore.Id, ProductId = p1.Id, QuantityOnHand = 100 };
                var s2 = new Stock { StoreId = defaultStore.Id, ProductId = p2.Id, QuantityOnHand = 50 };
                context.Stocks.AddRange(s1, s2);
                await context.SaveChangesAsync();
            }

            logger.LogInformation("Database seed completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }
}
