using ModManager.Ui.Services;

namespace ModManager.Tests.Ui.Services;

[TestClass]
public sealed class AdBlockServiceTests
{
    [TestMethod]
    public void ExtractBlockedHosts_WhenRuleIsPathScoped_ThenHostIsNotBlocked()
    {
        // Real EasyList rules like "||google.com/pagead/conversion_async.js" target
        // one ad-serving path on a major site, not the whole domain. Our
        // WKContentRuleList matching is host-only (no path awareness), so treating
        // these as "block google.com" would block the entire site — this is exactly
        // what caused google.com's logo/scripts to be blocked after ad-block rules
        // started actually compiling.
        string[] hosts = AdBlockService.ExtractBlockedHosts(
            "||google.com/pagead/conversion_async.js\n" +
            "||facebook.com/audiencenetwork/$third-party\n");

        CollectionAssert.DoesNotContain(hosts, "google.com");
        CollectionAssert.DoesNotContain(hosts, "facebook.com");
    }

    [TestMethod]
    public void ExtractBlockedHosts_WhenRuleIsDomainAnchored_ThenHostIsBlocked()
    {
        string[] hosts = AdBlockService.ExtractBlockedHosts("||ads.example.com^\n");

        CollectionAssert.Contains(hosts, "ads.example.com");
    }

    [TestMethod]
    public void ExtractBlockedHosts_WhenRuleIsDomainAnchoredWithOptions_ThenHostIsBlocked()
    {
        string[] hosts = AdBlockService.ExtractBlockedHosts("||tracker.example.com^$third-party\n");

        CollectionAssert.Contains(hosts, "tracker.example.com");
    }
}
