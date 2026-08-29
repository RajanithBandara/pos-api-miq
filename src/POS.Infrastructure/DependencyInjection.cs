using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Common.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Interfaces;
using POS.Infrastructure.Authentication;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Repositories;
using POS.Infrastructure.Services;

namespace POS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dbProvider = configuration["DatabaseProvider"] ?? "PostgreSQL";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // EF Core with PostgreSQL / Npgsql or InMemory for integration testing
        services.AddDbContext<AppDbContext>(options =>
        {
            if (dbProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) || connectionString == "InMemory")
            {
                var dbName = configuration["InMemoryDbName"] ?? "POS_TestDb_" + Guid.NewGuid();
                options.UseInMemoryDatabase(dbName);
            }
            else if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                });
            }
            else
            {
                options.UseInMemoryDatabase("POS_Default_InMemory");
            }
        });

        // Unit Of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped(typeof(IRepository<,>), typeof(GenericRepository<,>));
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ISyncRepository, SyncRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        // Specific entity repositories as generic
        services.AddScoped<IRepository<Store, Guid>, GenericRepository<Store, Guid>>();
        services.AddScoped<IRepository<PosTerminal, Guid>, GenericRepository<PosTerminal, Guid>>();
        services.AddScoped<IRepository<Role, Guid>, GenericRepository<Role, Guid>>();
        services.AddScoped<IRepository<Category, Guid>, GenericRepository<Category, Guid>>();
        services.AddScoped<IRepository<Employee, Guid>, GenericRepository<Employee, Guid>>();

        // Infrastructure Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
