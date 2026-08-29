using System.Collections.Generic;

namespace POS.Application.Common;

public static class Permissions
{
    public const string ViewDashboard = "Permissions.Dashboard.View";
    public const string ViewReports = "Permissions.Reports.View";
    public const string ManageEmployees = "Permissions.Employees.Manage";
    public const string ManageProducts = "Permissions.Products.Manage";
    public const string ManageInventory = "Permissions.Inventory.Manage";
    public const string ProcessSales = "Permissions.Sales.Process";
    public const string ViewSales = "Permissions.Sales.View";
    public const string ManageCustomers = "Permissions.Customers.Manage";
    public const string ManageUsers = "Permissions.Users.Manage";
    public const string ManageStores = "Permissions.Stores.Manage";
    public const string SynchronizeData = "Permissions.Sync.Execute";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ViewDashboard,
        ViewReports,
        ManageEmployees,
        ManageProducts,
        ManageInventory,
        ProcessSales,
        ViewSales,
        ManageCustomers,
        ManageUsers,
        ManageStores,
        SynchronizeData
    };
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string StoreManager = "StoreManager";
    public const string Cashier = "Cashier";
    public const string InventoryManager = "InventoryManager";
}
