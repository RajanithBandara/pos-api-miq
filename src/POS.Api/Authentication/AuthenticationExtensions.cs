using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using POS.Infrastructure.Authentication;

namespace POS.Api.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Registers both bearer schemes.
    ///
    /// The terminal scheme is the one that already existed, moved here unchanged and given a
    /// name. It stays the application default, so every endpoint that was authenticated before
    /// is authenticated by exactly the same handler, with exactly the same parameters, whether or
    /// not it names a scheme. Dashboard authentication is added alongside it and reaches only the
    /// endpoints whose policy asks for it.
    /// </summary>
    public static IServiceCollection AddPosAndDashboardAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var jwtSecret = jwtSection["SecretKey"] ?? DefaultSigningKey;

        if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
        {
            jwtSecret = DefaultSigningKey;
        }

        // Fetches Neon's public verification keys. Registered here rather than inside the options
        // so it goes through the factory and gets pooled handlers and connection reuse.
        services.AddHttpClient(NeonAuthDefaults.HttpClientName);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureNeonAuthJwtBearerOptions>();

        services.AddAuthentication(AuthenticationSchemes.Pos)
            .AddJwtBearer(AuthenticationSchemes.Pos, options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSection["Issuer"] ?? "POS-API",
                    ValidAudience = jwtSection["Audience"] ?? "POS-Clients",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),

                    // Terminal tokens are symmetric. Pinning the algorithm keeps a token signed
                    // by anything else — including one an attacker signed with a public key
                    // fetched from somewhere — from being offered against this key.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            // Configured by ConfigureNeonAuthJwtBearerOptions, which needs services this callback
            // cannot reach.
            .AddJwtBearer(AuthenticationSchemes.Neon);

        return services;
    }

    /// <summary>
    /// The signing key a deployment falls back to when none is configured. Public knowledge, and
    /// therefore not a secret: see the deployment notes for why configuring Jwt:SecretKey is not
    /// optional in production.
    /// </summary>
    private const string DefaultSigningKey =
        "POS_SUPER_SECRET_SECURITY_SIGNING_KEY_2026_PRODUCTION_MIQ_API_TOKEN";
}
