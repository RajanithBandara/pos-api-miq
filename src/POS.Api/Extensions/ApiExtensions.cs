using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using POS.Api.Authentication;

namespace POS.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "POS Web API",
                Version = "v1",
                Description =
                    "Central ASP.NET Core Web API for multi-store retail POS operations with Neon PostgreSQL " +
                    "connectivity and health checking.\n\n" +
                    "Two credentials are in play. Tills enroll with a single-use code, receive a device secret, " +
                    "and exchange it at /api/terminals/token for the bearer token every sync call carries. " +
                    "Provisioning endpoints instead take the out-of-band X-Provisioning-Key header.",
                Contact = new OpenApiContact
                {
                    Name = "POS Development Team"
                }
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Terminal token from POST /api/terminals/token. Enter the token only, without the \"Bearer \" prefix."
            });

            options.AddSecurityDefinition("ProvisioningKey", new OpenApiSecurityScheme
            {
                Name = RequireProvisioningKeyAttribute.HeaderName,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Bootstrap key for the provisioning endpoints, supplied out of band."
            });

            // OpenAPI.NET 2.x dropped OpenApiReference: a requirement is now keyed by a typed
            // scheme reference, which is resolved against the document it belongs to.
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer", document), [] },
                { new OpenApiSecuritySchemeReference("ProvisioningKey", document), [] }
            });
        });

        return services;
    }
}
