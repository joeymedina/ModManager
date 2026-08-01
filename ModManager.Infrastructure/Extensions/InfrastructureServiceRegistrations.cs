using Microsoft.Extensions.DependencyInjection;
using ModManager.Application.Interfaces;
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
        services.AddSingleton<WickedWhimsArchiveInstaller>();
        services.AddSingleton<IModUpdateStrategy, WickedWhimsUpdateStrategy>();

        return services;
    }
}
