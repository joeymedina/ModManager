using Avalonia;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ModManager.Ui;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Debug.WriteLine($"Unobserved task exception: {e.Exception}");

            // Prevent the exception from terminating the process
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            Debug.WriteLine($"Unhandled domain exception (terminating: {e.IsTerminating}): {exception}");
        };

        try
        {
            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        }
        catch(Exception ex)
        {
            Debug.Write("Application terminated unexpectedly.\nException: " + ex);
        }
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
