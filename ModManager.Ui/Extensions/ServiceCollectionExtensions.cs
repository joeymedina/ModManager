using Microsoft.Extensions.DependencyInjection;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ModPageViewModel>();
        services.AddTransient<MainViewModel>();
        return services;
    }
}
