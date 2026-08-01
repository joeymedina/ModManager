using Microsoft.Extensions.DependencyInjection;
using ModManager.Application.Interfaces;
using ModManager.Application.Services;

namespace ModManager.Application.Extensions;

public static class ApplicationServiceRegistrations
{
    /// <summary>
    /// Registers application-layer services.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IModUpdateOrchestrator, ModUpdateOrchestrator>();
        return services;
    }
}
