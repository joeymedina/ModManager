using System.Text.Json;
using ModManager.Ui.Services.Browser.Mac;

namespace ModManager.Tests.Ui.Services.Browser.Mac;

[TestClass]
public sealed class WKContentRuleListAdBlockerTests
{
    [TestMethod]
    public void BuildRuleListJson_WhenGivenBlockedHosts_ThenProducesOneWebKitBlockRulePerHost()
    {
#pragma warning disable CA1416 // Pure string/JSON logic; no macOS-specific call despite living in an OS-gated class.
        string json = WKContentRuleListAdBlocker.BuildRuleListJson(["ads.example.com", "tracker.test"]);
#pragma warning restore CA1416

        using JsonDocument document = JsonDocument.Parse(json);
        List<JsonElement> ruleList = [.. document.RootElement.EnumerateArray()];

        Assert.HasCount(2, ruleList);

        JsonElement firstTrigger = ruleList[0].GetProperty("trigger");
        string? urlFilter = firstTrigger.GetProperty("url-filter").GetString();
        Assert.IsTrue(urlFilter is not null && urlFilter.Contains("ads\\.example\\.com", StringComparison.Ordinal));
        Assert.IsTrue(firstTrigger.GetProperty("resource-type").EnumerateArray().Any(
            resourceType => resourceType.GetString() == "script"));
        Assert.AreEqual("block", ruleList[0].GetProperty("action").GetProperty("type").GetString());
    }

    [TestMethod]
    public void BuildRuleListJson_WhenGivenNoBlockedHosts_ThenProducesEmptyArray()
    {
#pragma warning disable CA1416
        string json = WKContentRuleListAdBlocker.BuildRuleListJson([]);
#pragma warning restore CA1416

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.AreEqual(0, document.RootElement.GetArrayLength());
    }
}
