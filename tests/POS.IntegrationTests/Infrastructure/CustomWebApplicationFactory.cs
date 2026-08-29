using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace POS.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "POS_Integration_TestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseProvider", "InMemory");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemory");
        builder.UseSetting("InMemoryDbName", _dbName);
        builder.UseSetting("Jwt:SecretKey", "INTEGRATION_TEST_SUPER_SECRET_KEY_32_BYTES_LONG");
        builder.UseSetting("Jwt:Issuer", "POS-API-TEST");
        builder.UseSetting("Jwt:Audience", "POS-Clients-TEST");

        builder.UseEnvironment("Testing");
    }
}
