using ModManager.Application.Models;
using ModManager.Infrastructure.Services;
using ModManager.Tests.Application.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
public sealed class SiteTrackingResolverTests
{
    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenTheUrlResolvesAndTheSiteReturnsAVersion_ThenReturnsIt()
    {
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("ZombieApocalypseDownload"), "2.3.1", null, "Zombie Apocalypse", null, null)],
            resolveModKey: _ => new SiteModKey("ZombieApocalypseDownload"));
        var resolver = new SiteTrackingResolver([strategy]);

        string? version = await resolver.TryFetchCurrentVersionAsync(
            "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "Zombie Apocalypse", CancellationToken.None);

        Assert.AreEqual("2.3.1", version);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenNoStrategyMatchesTheHost_ThenReturnsNull()
    {
        var resolver = new SiteTrackingResolver([]);

        string? version = await resolver.TryFetchCurrentVersionAsync("https://example.com/mod", "Some Mod", CancellationToken.None);

        Assert.IsNull(version);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenNoUrlIsGiven_ThenReturnsNullWithoutFetching()
    {
        var strategy = new StubModSiteStrategy("sacrificialmods.com", []);
        var resolver = new SiteTrackingResolver([strategy]);

        string? version = await resolver.TryFetchCurrentVersionAsync(null, "Some Mod", CancellationToken.None);

        Assert.IsNull(version);
        Assert.IsEmpty(strategy.FetchCalls);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenTheStrategyCannotResolveAKey_ThenReturnsNullWithoutFetching()
    {
        var strategy = new StubModSiteStrategy("sacrificialmods.com", [], resolveModKey: _ => null);
        var resolver = new SiteTrackingResolver([strategy]);

        string? version = await resolver.TryFetchCurrentVersionAsync(
            "https://sacrificialmods.com/downloads.html", "Some Mod", CancellationToken.None);

        Assert.IsNull(version);
        Assert.IsEmpty(strategy.FetchCalls);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenTheStrategyThrows_ThenReturnsNullRatherThanPropagating()
    {
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [],
            resolveModKey: _ => new SiteModKey("ZombieApocalypseDownload"),
            throwOnFetch: new InvalidOperationException("site redesigned"));
        var resolver = new SiteTrackingResolver([strategy]);

        string? version = await resolver.TryFetchCurrentVersionAsync(
            "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "Zombie Apocalypse", CancellationToken.None);

        Assert.IsNull(version);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenTheFetchHangsPastItsTimeout_ThenReturnsNullWithoutWaitingForTheDefaultTimeout()
    {
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [],
            resolveModKey: _ => new SiteModKey("ZombieApocalypseDownload"),
            hangUntilCanceled: true);
        var resolver = new SiteTrackingResolver([strategy], fetchTimeout: TimeSpan.FromMilliseconds(50));

        string? version = await resolver.TryFetchCurrentVersionAsync(
            "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "Zombie Apocalypse", CancellationToken.None);

        Assert.IsNull(version);
    }

    [TestMethod]
    public async Task TryFetchCurrentVersionAsync_WhenTheCallerCancels_ThenPropagatesCancellationRatherThanReturningNull()
    {
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [],
            resolveModKey: _ => new SiteModKey("ZombieApocalypseDownload"),
            hangUntilCanceled: true);
        var resolver = new SiteTrackingResolver([strategy], fetchTimeout: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => resolver.TryFetchCurrentVersionAsync("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "Zombie Apocalypse", cts.Token));
    }
}
