using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POS.Infrastructure.Persistence;

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

        return services;
    }
}
