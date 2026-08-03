using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ModManager.Application.Extensions;
using ModManager.Infrastructure.Extensions;
using ModManager.Ui.Extensions;
using ModManager.Ui.ViewModels;
using ModManager.Ui.Views;
using System;
using System.Diagnostics;
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
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceCollection collection = new();
            collection.AddApplicationServices();
            collection.AddInfrastructureServices();
            collection.AddUiServices();

            IServiceProvider services = collection.BuildServiceProvider();
           
            MainViewModel vm = services.GetRequiredService<MainViewModel>();

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception
        Debug.WriteLine($"Unhandled UI thread exception: {e.Exception}");

        // Optionally prevent the application from crashing
        e.Handled = true;
    }
}