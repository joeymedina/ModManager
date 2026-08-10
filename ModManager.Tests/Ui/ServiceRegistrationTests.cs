using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModManager.Application.Extensions;
using ModManager.Infrastructure.Extensions;
using ModManager.Ui.Extensions;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui;

[TestClass]
public sealed class ServiceRegistrationTests
{
    /// <summary>
    /// Mirrors App.OnFrameworkInitializationCompleted. Services take an optional ILogger, so a missing
    /// logging registration or an unresolvable constructor only shows up when the container builds the
    /// real graph — which otherwise means a crash on launch rather than a failing test.
    /// </summary>
    [TestMethod]
    public void BuildServiceProvider_WhenMirroringAppStartup_ThenResolvesMainViewModel()
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddApplicationServices();
        collection.AddInfrastructureServices();
        collection.AddUiServices();

        using ServiceProvider services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.IsNotNull(services.GetRequiredService<MainViewModel>());
        Assert.IsNotNull(services.GetRequiredService<ModsPageViewModel>());
        Assert.IsNotNull(services.GetRequiredService<BrowsePageViewModel>());
    }
}
