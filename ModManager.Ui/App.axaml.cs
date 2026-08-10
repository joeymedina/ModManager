using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModManager.Application.Extensions;
using ModManager.Infrastructure.Extensions;
using ModManager.Ui.Extensions;
using ModManager.Ui.Models;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;
using ModManager.Ui.Views;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ModManager.Ui;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceCollection collection = new();
            collection.AddLogging(builder => builder.AddSerilog());
            collection.AddApplicationServices();
            collection.AddInfrastructureServices();
            collection.AddUiServices();

            IServiceProvider services = collection.BuildServiceProvider();

            ILogger<App> logger = services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Service provider built");

            ThemeService themeService = services.GetRequiredService<ThemeService>();
            string? themeName = services.GetRequiredService<SettingsStore>().Load().ThemeName;
            AppTheme theme = themeService.ListThemes().FirstOrDefault(t => t.Name == themeName) ?? ThemePresets.DefaultLight;
            themeService.Apply(theme);

            MainViewModel vm = services.GetRequiredService<MainViewModel>();

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            logger.LogInformation("Main window created");
        }

        base.OnFrameworkInitializationCompleted();
    }
}