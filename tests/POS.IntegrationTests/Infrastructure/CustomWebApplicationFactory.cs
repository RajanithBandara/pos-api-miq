using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using POS.Api.Authentication;

namespace POS.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Key the provisioning tests present. Only ever exists inside the test host.</summary>
    public const string ProvisioningKey = "integration-test-provisioning-key";

    private readonly string _dbName = "POS_Integration_TestDb_" + Guid.NewGuid();

    /// <summary>
    /// Issues the dashboard tokens the tests present. Its public key is the only one this host
    /// trusts, so a token from anywhere else fails exactly as it would in production.
    /// </summary>
    public NeonAuthTestIdentityProvider NeonAuth { get; } = new();

    /// <summary>
    /// Whether this host has dashboard authentication configured at all. Overridden by
    /// <see cref="NeonAuthUnconfiguredWebApplicationFactory"/> to prove the unconfigured case
    /// fails closed.
    /// </summary>
    protected virtual bool NeonAuthConfigured => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseProvider", "InMemory");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemory");
        builder.UseSetting("InMemoryDbName", _dbName);
        builder.UseSetting("Jwt:SecretKey", "INTEGRATION_TEST_SUPER_SECRET_KEY_32_BYTES_LONG");
        builder.UseSetting("Jwt:Issuer", "POS-API-TEST");
        builder.UseSetting("Jwt:Audience", "POS-Clients-TEST");
        builder.UseSetting("Provisioning:ApiKey", ProvisioningKey);

        builder.UseEnvironment("Testing");

        if (!NeonAuthConfigured) return;

        // Issuer and audience are derived from this exactly as they are in production.
        builder.UseSetting("NeonAuth:BaseUrl", NeonAuthTestIdentityProvider.AuthUrl);

        builder.ConfigureTestServices(services =>
        {
            // The only thing replaced is where the signing keys come from: a fixed set instead of
            // an HTTP fetch from Neon. Every check that decides whether a token is acceptable —
            // signature, algorithm, issuer, audience, expiry — still runs, through the same
            // handler and the same Ed25519 verification path production uses.
            services.PostConfigure<JwtBearerOptions>(AuthenticationSchemes.Neon, options =>
            {
                var configuration = new OpenIdConnectConfiguration();
                configuration.SigningKeys.Add(NeonAuth.PublicSigningKey);

                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}

/// <summary>
/// A host with no Neon Auth configuration, used to show that a deployment which forgot to set it
/// refuses dashboard requests rather than serving them unguarded.
/// </summary>
public sealed class NeonAuthUnconfiguredWebApplicationFactory : CustomWebApplicationFactory
{
    protected override bool NeonAuthConfigured => false;
}
