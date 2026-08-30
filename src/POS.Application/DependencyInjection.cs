using Microsoft.Extensions.DependencyInjection;
using POS.Application.Provisioning;
using POS.Application.Sync;
using POS.Application.Terminals;

namespace POS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITerminalEnrollmentService, TerminalEnrollmentService>();
        services.AddScoped<IProvisioningService, ProvisioningService>();
        services.AddScoped<ISyncIngestService, SyncIngestService>();

        return services;
    }
}
