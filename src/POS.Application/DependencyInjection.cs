using Microsoft.Extensions.DependencyInjection;
using POS.Application.Dashboard;
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
        services.AddScoped<POS.Application.Licensing.ILicensingService, POS.Application.Licensing.LicensingService>();

        // Dashboard use cases. They share the same repositories and the same database as the
        // till-facing ones — the web client is another consumer of this layer, not another
        // application.
        services.AddScoped<IDashboardUserDirectory, DashboardUserDirectory>();
        services.AddScoped<IDashboardAccessService, DashboardAccessService>();
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();

        return services;
    }
}
