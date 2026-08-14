using Microsoft.Extensions.DependencyInjection;
using ModManager.Application.Interfaces;
using ModManager.Infrastructure.Services;
using ModManager.Infrastructure.Services.Sacrificial;
using ModManager.Infrastructure.Services.WickedWhims;

namespace ModManager.Infrastructure.Extensions;

public static class InfrastructureServiceRegistrations
{
    /// <summary>
    /// Registers infrastructure-layer services.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WickedWhimsVersionDetector>();
        services.AddSingleton<WickedWhimsReleaseClient>();
        services.AddSingleton<IModUpdateStrategy, WickedWhimsUpdateStrategy>();

        services.AddSingleton<ModsFolderPathService>();
        services.AddSingleton<ModsDiscoveryService>();
        services.AddSingleton<ModsFileOperationsService>();
        services.AddSingleton<ModsManifestService>();
        services.AddSingleton<IModsFolderRepository, ModsFolderService>();
        services.AddSingleton<IArchiveInstallService, ArchiveInstallService>();

        services.AddSingleton<IUpdateCheckStateStore, UpdateCheckStateStore>();
        services.AddSingleton<IModPageFetcher, HttpModPageFetcher>();
        services.AddSingleton<IModSiteStrategy, SacrificialSiteStrategy>();

        return services;
    }
}
