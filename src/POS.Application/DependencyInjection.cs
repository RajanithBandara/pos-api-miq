using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Analytics.Services;
using POS.Application.Authentication.Services;
using POS.Application.Customers.Services;
using POS.Application.Employees.Services;
using POS.Application.Inventory.Services;
using POS.Application.Products.Services;
using POS.Application.Reports.Services;
using POS.Application.Sales.Services;
using POS.Application.Synchronization.Services;

namespace POS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Core Application Services
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISyncEngineService, SyncEngineService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IFifoFefoAllocationStrategy, FifoFefoAllocationStrategy>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
