using Avalonia;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace ModManager.Ui;

sealed class Program
{
    /// <summary>
    /// Log level, overridable without a rebuild so a user hitting a bug can set
    /// MODMANAGER_LOG_LEVEL=Debug, reproduce, and send the file. Debug logs per-file extraction and
    /// hashes, so it is deliberately not the default.
    /// </summary>
    private static LogEventLevel ResolveMinimumLevel() =>
        Enum.TryParse(Environment.GetEnvironmentVariable("MODMANAGER_LOG_LEVEL"), ignoreCase: true, out LogEventLevel level)
            ? level
            : LogEventLevel.Information;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModManager",
            "logs",
            "app-.log");

        LogEventLevel minimumLevel = ResolveMinimumLevel();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        Log.Information("Application starting at {LogLevel} level (log file: {LogPath})", minimumLevel, logPath);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled domain exception (terminating: {IsTerminating})", e.IsTerminating);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.Information("Application exiting");
            Log.CloseAndFlush();
        }
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
