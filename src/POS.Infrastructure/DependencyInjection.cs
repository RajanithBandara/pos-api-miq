using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Common.Interfaces;
using POS.Domain.Interfaces;
using POS.Infrastructure.Authentication;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Repositories;

namespace POS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dbProvider = configuration["DatabaseProvider"] ?? "PostgreSQL";
        var rawConnectionString = configuration.GetConnectionString("DefaultConnection");

        // Accepts either the provider's URI form or Npgsql's key-value form. See
        // PostgresConnectionString for why both turn up in practice.
        var connectionString = rawConnectionString == "InMemory"
            ? rawConnectionString
            : PostgresConnectionString.Normalise(rawConnectionString);

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

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<ITerminalRepository, TerminalRepository>();
        services.AddScoped<IEnrollmentCodeRepository, EnrollmentCodeRepository>();
        services.AddScoped<ISyncEventRepository, SyncEventRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<ISecretHasher, BcryptSecretHasher>();
        services.AddSingleton<ITerminalTokenService, JwtTerminalTokenService>();

        return services;
    }
}
