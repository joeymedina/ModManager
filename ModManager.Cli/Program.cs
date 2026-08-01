using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModManager.Application.Extensions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Infrastructure.Extensions;

namespace ModManager.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                CliOptions.PrintUsage();
                return 0;
            }

            IConfigurationRoot environmentConfiguration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
            string runtimeEnvironment = environmentConfiguration["RUNTIME_ENVIRONMENT"] ?? string.Empty;

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            if (!string.IsNullOrEmpty(runtimeEnvironment))
            {
                builder.Configuration.AddJsonFile($"appsettings.{runtimeEnvironment}.json", optional: true, reloadOnChange: true);
            }

            builder.Services.AddApplicationServices();
            builder.Services.AddInfrastructureServices();

            using IHost host = builder.Build();
            IModUpdateOrchestrator orchestrator = host.Services.GetRequiredService<IModUpdateOrchestrator>();

            ModUpdateResult result = await orchestrator.ExecuteAsync(
                new ModUpdateRequest(options.ModId, options.Folder, options.Download),
                CancellationToken.None);

            PrintResult(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation was canceled.");
            return 130;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
    }

    private static void PrintResult(ModUpdateResult result)
    {
        Console.WriteLine($"Installed: v{result.InstalledVersion.Version} ({result.InstalledVersion.Source})");
        Console.WriteLine($"Latest:    v{result.LatestRelease.Version}");

        if (result.VersionComparison >= 0)
        {
            Console.WriteLine(result.VersionComparison == 0
                ? "Already up to date."
                : "Installed version is newer than the official release.");
            return;
        }

        if (!result.DownloadPerformed)
        {
            Console.WriteLine("Update available. Run with --download to install it.");
            return;
        }

        Console.WriteLine($"Installed {result.InstalledFileCount} files into mods folder.");
    }
}
