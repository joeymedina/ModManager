using Microsoft.Extensions.DependencyInjection;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<BrowserDownloadService>();
        services.AddTransient<ModsPageViewModel>();
        services.AddTransient<UpdatesPageViewModel>();
        services.AddTransient<BrowsePageViewModel>();
        services.AddTransient<MainViewModel>();
        return services;
    }
}
