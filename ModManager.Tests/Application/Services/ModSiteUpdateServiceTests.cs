using ModManager.Application.Models;
using ModManager.Application.Services;

namespace ModManager.Tests.Application.Services;

[TestClass]
public sealed class ModSiteUpdateServiceTests
{
    [TestMethod]
    public async Task CheckAsync_WhenNoStrategyRegisteredForSite_ThenReturnsIndeterminate()
    {
        TrackedMod mod = CreateTrackedMod("unknown-site", "key-1", "1.0", null);
        var service = new ModSiteUpdateService([], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual(SiteUpdateStatus.Indeterminate, results[0].Status);
        StringAssert.Contains(results[0].Reason, "No update strategy is registered");
    }

    [TestMethod]
    public async Task CheckAsync_WhenObservedVersionMatchesBaselineAfterNormalization_ThenReturnsUpToDate()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "2.3.1", null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "v2.3.1", null, "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.UpToDate, results[0].Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenObservedVersionDiffersFromBaseline_ThenReturnsUpdateAvailable()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "2.3.1", null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "2.3.2", null, "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.UpdateAvailable, results[0].Status);
        Assert.IsTrue(results[0].IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_WhenNoVersionButDatesDiffer_ThenReturnsUpdateAvailable()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", null, "10-12-2025");
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), null, "03-16-2026", "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.UpdateAvailable, results[0].Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenNoVersionAndDatesMatch_ThenReturnsUpToDate()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", null, "10-12-2025");
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), null, "10-12-2025", "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.UpToDate, results[0].Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenNeitherVersionNorDateIsComparable_ThenReturnsIndeterminate()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", null, null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), null, null, "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.Indeterminate, results[0].Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenResolvedKeyHasNoMatchingObservation_ThenReturnsIndeterminate()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "1.0", null);
        var strategy = new StubModSiteStrategy("sacrificialmods.com", []);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.Indeterminate, results[0].Status);
        StringAssert.Contains(results[0].Reason, "Not found on the site");
    }

    [TestMethod]
    public async Task CheckAsync_WhenModKeyCannotBeResolved_ThenReturnsIndeterminateAndSkipsTheFetchForIt()
    {
        TrackedMod unresolvable = CreateTrackedMod("sacrificialmods.com", null, "1.0", null, trackingUrl: "https://sacrificialmods.com/downloads.html");
        TrackedMod resolvable = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "1.0", null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "1.0", null, "Zombie Apocalypse", null, null)],
            resolveModKey: _ => null);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([unresolvable, resolvable], CancellationToken.None);

        SiteUpdateCheckResult unresolvedResult = results.Single(result => result.InstallId == unresolvable.Record.InstallId);
        Assert.AreEqual(SiteUpdateStatus.Indeterminate, unresolvedResult.Status);
        StringAssert.Contains(unresolvedResult.Reason, "link a mod page");

        Assert.HasCount(1, strategy.FetchCalls);
        Assert.HasCount(1, strategy.FetchCalls[0]);
        Assert.AreEqual("zombie-apocalypse", strategy.FetchCalls[0][0].Value);
    }

    [TestMethod]
    public async Task CheckAsync_WhenModKeyIsNewlyResolvedThisCheck_ThenReportsItInTheResult()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", null, "1.0", null, trackingUrl: "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload");
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "1.0", null, "Zombie Apocalypse", null, null)],
            resolveModKey: hints => hints.ModPageUrl!.EndsWith("#ZombieApocalypseDownload") ? new SiteModKey("zombie-apocalypse") : null);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual("zombie-apocalypse", results[0].ResolvedModKey?.Value);
    }

    [TestMethod]
    public async Task CheckAsync_WhenModKeyWasAlreadyResolved_ThenResolvedModKeyIsNullInResult()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "1.0", null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "1.0", null, "Zombie Apocalypse", null, null)]);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.IsNull(results[0].ResolvedModKey);
    }

    [TestMethod]
    public async Task CheckAsync_WhenAStrategyThrows_ThenOnlyThatSitesModsBecomeIndeterminate()
    {
        TrackedMod brokenSiteMod = CreateTrackedMod("broken-site", "key-1", "1.0", null);
        TrackedMod healthySiteMod = CreateTrackedMod("healthy-site", "key-2", "1.0", null);

        var brokenStrategy = new StubModSiteStrategy("broken-site", [], throwOnFetch: new InvalidOperationException("scraper broke"));
        var healthyStrategy = new StubModSiteStrategy(
            "healthy-site",
            [new SiteObservation(new SiteModKey("key-2"), "1.0", null, "Healthy Mod", null, null)]);

        var service = new ModSiteUpdateService([brokenStrategy, healthyStrategy], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([brokenSiteMod, healthySiteMod], CancellationToken.None);

        SiteUpdateCheckResult brokenResult = results.Single(result => result.InstallId == brokenSiteMod.Record.InstallId);
        Assert.AreEqual(SiteUpdateStatus.Indeterminate, brokenResult.Status);
        StringAssert.Contains(brokenResult.Reason, "scraper broke");

        SiteUpdateCheckResult healthyResult = results.Single(result => result.InstallId == healthySiteMod.Record.InstallId);
        Assert.AreEqual(SiteUpdateStatus.UpToDate, healthyResult.Status);
    }

    [TestMethod]
    public async Task CheckAsync_WhenAStrategyHangsPastItsTimeout_ThenReturnsIndeterminateWithoutWaitingForIt()
    {
        TrackedMod mod = CreateTrackedMod("slow-site", "key-1", "1.0", null);
        var strategy = new StubModSiteStrategy("slow-site", [], hangUntilCanceled: true);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore(), strategyTimeout: TimeSpan.FromMilliseconds(50));

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([mod], CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.Indeterminate, results[0].Status);
        StringAssert.Contains(results[0].Reason, "did not respond in time");
    }

    [TestMethod]
    public async Task CheckAsync_WhenCallerCancels_ThenPropagatesCancellationRatherThanReturningIndeterminate()
    {
        TrackedMod mod = CreateTrackedMod("slow-site", "key-1", "1.0", null);
        var strategy = new StubModSiteStrategy("slow-site", [], hangUntilCanceled: true);
        var service = new ModSiteUpdateService([strategy], new InMemoryUpdateCheckStateStore(), strategyTimeout: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => service.CheckAsync([mod], cts.Token));
    }

    [TestMethod]
    public async Task CheckAsync_WhenSitesAreMixed_ThenEachStrategyOnlyReceivesItsOwnSitesKeys()
    {
        TrackedMod siteAMod = CreateTrackedMod("site-a", "a-key", "1.0", null);
        TrackedMod siteBMod = CreateTrackedMod("site-b", "b-key", "1.0", null);

        var strategyA = new StubModSiteStrategy("site-a", [new SiteObservation(new SiteModKey("a-key"), "1.0", null, null, null, null)]);
        var strategyB = new StubModSiteStrategy("site-b", [new SiteObservation(new SiteModKey("b-key"), "1.0", null, null, null, null)]);

        var service = new ModSiteUpdateService([strategyA, strategyB], new InMemoryUpdateCheckStateStore());

        await service.CheckAsync([siteAMod, siteBMod], CancellationToken.None);

        Assert.AreEqual("a-key", strategyA.FetchCalls.Single().Single().Value);
        Assert.AreEqual("b-key", strategyB.FetchCalls.Single().Single().Value);
    }

    [TestMethod]
    public async Task CheckAsync_WhenRecordHasNoTracking_ThenIsSkipped()
    {
        InstallRecord untracked = new("install-1", new InstallSource("manual", null, null), null, DateTime.UtcNow, null, [], []);
        var service = new ModSiteUpdateService([], new InMemoryUpdateCheckStateStore());

        IReadOnlyList<SiteUpdateCheckResult> results = await service.CheckAsync([new TrackedMod(untracked, "Untracked Mod")], CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task CheckAsync_WhenCalled_ThenPersistsCheckState()
    {
        TrackedMod mod = CreateTrackedMod("sacrificialmods.com", "zombie-apocalypse", "2.3.1", null);
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [new SiteObservation(new SiteModKey("zombie-apocalypse"), "2.3.2", null, "Zombie Apocalypse", null, null)]);
        var stateStore = new InMemoryUpdateCheckStateStore();
        var service = new ModSiteUpdateService([strategy], stateStore);

        await service.CheckAsync([mod], CancellationToken.None);

        Assert.IsTrue(stateStore.Saved.ContainsKey(mod.Record.InstallId));
        Assert.AreEqual(SiteUpdateStatus.UpdateAvailable, stateStore.Saved[mod.Record.InstallId].LastStatus);
        Assert.AreEqual("2.3.2", stateStore.Saved[mod.Record.InstallId].LastObservedVersion);
    }

    private static TrackedMod CreateTrackedMod(
        string siteKey,
        string? siteModKey,
        string? baselineVersion,
        string? baselineUpdatedOnRaw,
        string? trackingUrl = null,
        string? installId = null)
    {
        InstallRecord record = new(
            installId ?? Guid.NewGuid().ToString("N"),
            new InstallSource("browser", trackingUrl ?? $"https://{siteKey}/page", null),
            baselineVersion,
            DateTime.UtcNow,
            null,
            [new InstallRecordFile("Zombie Apocalypse/Main.package", "abc", 1)],
            [],
            new UpdateTracking(siteKey, siteModKey, trackingUrl ?? $"https://{siteKey}/page", baselineVersion, baselineUpdatedOnRaw, DateTime.UtcNow));

        return new TrackedMod(record, "Zombie Apocalypse");
    }
}
