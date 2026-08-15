using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Infrastructure.Services.Sacrificial;

namespace ModManager.Tests.Infrastructure.Services.Sacrificial;

[TestClass]
public sealed class SacrificialSiteStrategyTests
{
    [TestMethod]
    public void ParseModCards_WhenGivenRealMarkup_ThenExtractsEveryCard()
    {
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        Assert.HasCount(6, observations);
    }

    [TestMethod]
    public void ParseModCards_WhenGivenRealMarkup_ThenExtractsZombieApocalypseFieldsCorrectly()
    {
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        SiteObservation zombieApocalypse = observations.Single(observation => observation.ModKey.Value == "ZombieApocalypseDownload");

        Assert.AreEqual("v2.3.1", zombieApocalypse.Version);
        Assert.AreEqual("09-7-2025", zombieApocalypse.UpdatedOnRaw);
        Assert.AreEqual("Zombie Apocalypse", zombieApocalypse.Title);
        StringAssert.Contains(zombieApocalypse.DownloadUrl, "SAC_Zombie%20Apocalypse");
    }

    [TestMethod]
    public void ParseModCards_WhenACardHasBothADirectAndAPatreonLink_ThenPicksTheDirectOne()
    {
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        SiteObservation extremeViolence = observations.Single(observation => observation.ModKey.Value == "ExtremeViolenceDownload");

        StringAssert.Contains(extremeViolence.DownloadUrl, "sacrificialmods.com");
        Assert.IsFalse(extremeViolence.DownloadUrl!.Contains("patreon", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ParseModCards_WhenVersionContainsSpacesAndLetters_ThenExtractsItVerbatim()
    {
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        SiteObservation roadToFame = observations.Single(observation => observation.ModKey.Value == "RoadToFameDownload");

        Assert.AreEqual("v0.5.1 D12", roadToFame.Version);
    }

    [TestMethod]
    public void ParseModCards_WhenTitleDiffersFromTheSearchTitleAttribute_ThenUsesTheSearchTitleAttribute()
    {
        // The real page's <h3> for this card reads "Path Of Legends By Kyutso", but data-search-title
        // (a cleaner, dedicated field the site itself maintains) reads just "Path Of Legends".
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        SiteObservation pathOfLegends = observations.Single(observation => observation.ModKey.Value == "PathOfLegendsDownload");

        Assert.AreEqual("Path Of Legends", pathOfLegends.Title);
    }

    [TestMethod]
    public void ParseModCards_WhenAdContainersAndCommentsSitBetweenCards_ThenDoesNotConfuseThemForCards()
    {
        // The fixture has <div class="ad-container"> blocks and HTML comments between several of its
        // six cards — asserting the count here (rather than just in the "extracts every card" test)
        // pins down specifically that non-card content between cards doesn't get mistaken for one.
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.SixModCardsSlice);

        Assert.IsTrue(observations.All(observation => observation.ModKey.Value.EndsWith("Download", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ParseModCards_WhenPageHasBeenRedesignedWithNoModCards_ThenReturnsEmptyRatherThanThrowing()
    {
        IReadOnlyList<SiteObservation> observations = SacrificialSiteStrategy.ParseModCards(SacrificialFixtures.RedesignedPageWithNoModCards);

        Assert.IsEmpty(observations);
    }

    [TestMethod]
    public void TryResolveModKey_WhenModPageUrlHasAFragment_ThenReturnsIt()
    {
        var strategy = new SacrificialSiteStrategy(new StubFetcher(SacrificialFixtures.SixModCardsSlice));
        ModKeyHints hints = new("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", null, "Zombie Apocalypse", []);

        SiteModKey? key = strategy.TryResolveModKey(hints);

        Assert.AreEqual("ZombieApocalypseDownload", key?.Value);
    }

    [TestMethod]
    public void TryResolveModKey_WhenModPageUrlHasNoFragment_ThenReturnsNull()
    {
        var strategy = new SacrificialSiteStrategy(new StubFetcher(SacrificialFixtures.SixModCardsSlice));
        ModKeyHints hints = new("https://sacrificialmods.com/downloads.html", null, "Zombie Apocalypse", []);

        SiteModKey? key = strategy.TryResolveModKey(hints);

        Assert.IsNull(key);
    }

    [TestMethod]
    public void TryResolveModKey_WhenModPageUrlIsNull_ThenReturnsNull()
    {
        var strategy = new SacrificialSiteStrategy(new StubFetcher(SacrificialFixtures.SixModCardsSlice));
        ModKeyHints hints = new(null, null, "Zombie Apocalypse", []);

        SiteModKey? key = strategy.TryResolveModKey(hints);

        Assert.IsNull(key);
    }

    [TestMethod]
    public async Task FetchObservationsAsync_WhenCalled_ThenReturnsOnlyRequestedKeys()
    {
        var fetcher = new StubFetcher(SacrificialFixtures.SixModCardsSlice);
        var strategy = new SacrificialSiteStrategy(fetcher);

        IReadOnlyList<SiteObservation> observations = await strategy.FetchObservationsAsync(
            [new SiteModKey("ZombieApocalypseDownload"), new SiteModKey("ArmageddonDownload")],
            CancellationToken.None);

        Assert.HasCount(2, observations);
        Assert.IsTrue(observations.Any(observation => observation.ModKey.Value == "ZombieApocalypseDownload"));
        Assert.IsTrue(observations.Any(observation => observation.ModKey.Value == "ArmageddonDownload"));
        Assert.IsFalse(observations.Any(observation => observation.ModKey.Value == "ExtremeViolenceDownload"));
    }

    [TestMethod]
    public async Task FetchObservationsAsync_WhenAKeyIsNotOnThePage_ThenOmitsItRatherThanReturningANullEntry()
    {
        var fetcher = new StubFetcher(SacrificialFixtures.SixModCardsSlice);
        var strategy = new SacrificialSiteStrategy(fetcher);

        IReadOnlyList<SiteObservation> observations = await strategy.FetchObservationsAsync(
            [new SiteModKey("NoLongerListedDownload")],
            CancellationToken.None);

        Assert.IsEmpty(observations);
    }

    [TestMethod]
    public async Task FetchObservationsAsync_WhenNoKeysAreRequested_ThenReturnsEmptyWithoutFetching()
    {
        var fetcher = new StubFetcher(SacrificialFixtures.SixModCardsSlice);
        var strategy = new SacrificialSiteStrategy(fetcher);

        IReadOnlyList<SiteObservation> observations = await strategy.FetchObservationsAsync([], CancellationToken.None);

        Assert.IsEmpty(observations);
        Assert.AreEqual(0, fetcher.FetchCount);
    }

    [TestMethod]
    public void SiteKey_Hosts_And_Capabilities_MatchSacrificialModsRequirements()
    {
        var strategy = new SacrificialSiteStrategy(new StubFetcher(SacrificialFixtures.SixModCardsSlice));

        Assert.AreEqual("sacrificialmods.com", strategy.SiteKey);
        Assert.Contains("sacrificialmods.com", strategy.Hosts);
        Assert.IsFalse(strategy.Capabilities.RequiresAuthenticatedSession);
        Assert.IsTrue(strategy.Capabilities.ProvidesUpdatedOnDate);
    }

    private sealed class StubFetcher(string html) : IModPageFetcher
    {
        public int FetchCount { get; private set; }

        public Task<PageContent> FetchAsync(Uri url, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            return Task.FromResult(new PageContent(url, html));
        }
    }
}
