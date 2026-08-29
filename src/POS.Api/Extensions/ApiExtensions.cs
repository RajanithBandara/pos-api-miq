using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

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
                Description = "Central ASP.NET Core Web API for multi-store retail POS operations with Neon PostgreSQL connectivity and health checking.",
                Contact = new OpenApiContact
                {
                    Name = "POS Development Team"
                }
            });
        });

        return services;
    }
}
