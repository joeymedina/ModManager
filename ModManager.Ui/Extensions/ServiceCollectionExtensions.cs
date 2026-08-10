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
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        // The Settings page mutates the Mods page's folder, so both must see the same instance.
        services.AddSingleton<ModsPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddTransient<UpdatesPageViewModel>();
        services.AddTransient<BrowsePageViewModel>();
        services.AddTransient<MainViewModel>();
        return services;
    }
}
