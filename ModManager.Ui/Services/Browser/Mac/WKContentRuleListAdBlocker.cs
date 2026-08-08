using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModManager.Ui.Services.Browser.Mac.Interop;

namespace ModManager.Ui.Services.Browser.Mac;

// Real, WebKit-native ad blocking for macOS, replacing the previous JS
// monkey-patch. Avalonia's own cross-platform WebResourceRequested event is
// raised from decidePolicyForNavigationAction (confirmed by reading Avalonia's
// MaciosWebViewAdapter/WKNavigationDelegate source) which only fires for
// navigations, never subresource loads — so it cannot block most ad requests.
// WKContentRuleList is WebKit's compiled content-blocker ruleset, the same
// mechanism Safari content blockers use, and runs inside WebKit's own
// networking layer before a request is ever made.
[SupportedOSPlatform("macos")]
internal static class WKContentRuleListAdBlocker
{
    private const string Identifier = "com.modmanager.adblock";

    private static readonly IntPtr StoreClass = Libobjc.objc_getClass("WKContentRuleListStore");
    private static readonly IntPtr DefaultStoreSel = Libobjc.sel_getUid("defaultStore");
    private static readonly IntPtr CompileSel =
        Libobjc.sel_getUid("compileContentRuleListForIdentifier:encodedContentRuleList:completionHandler:");
    private static readonly IntPtr ConfigurationSel = Libobjc.sel_getUid("configuration");
    private static readonly IntPtr UserContentControllerSel = Libobjc.sel_getUid("userContentController");
    private static readonly IntPtr AddContentRuleListSel = Libobjc.sel_getUid("addContentRuleList:");

    private static readonly unsafe IntPtr CompletionCallback =
        new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnCompiled);

    public static void Apply(IntPtr webViewHandle, IReadOnlyList<string> blockedHosts)
    {
        if (blockedHosts.Count == 0)
        {
            return;
        }

        IntPtr store = Libobjc.IntPtr_msgSend(StoreClass, DefaultStoreSel);
        IntPtr identifier = NativeString.Create(Identifier);
        IntPtr rulesJson = NativeString.Create(BuildRuleListJson(blockedHosts));
        IntPtr completion = BlockLiteral.GetBlockForFunctionPointer(CompletionCallback, webViewHandle);

        Libobjc.Void_msgSend(store, CompileSel, identifier, rulesJson, completion);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCompiled(IntPtr block, IntPtr contentRuleList, IntPtr error)
    {
        if (contentRuleList == IntPtr.Zero)
        {
            Debug.WriteLine("WKContentRuleList compilation failed.");
            return;
        }

        IntPtr webViewHandle = BlockLiteral.TryGetBlockState(block);
        if (webViewHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr configuration = Libobjc.IntPtr_msgSend(webViewHandle, ConfigurationSel);
        IntPtr userContentController = Libobjc.IntPtr_msgSend(configuration, UserContentControllerSel);
        Libobjc.Void_msgSend(userContentController, AddContentRuleListSel, contentRuleList);
    }

    internal static string BuildRuleListJson(IReadOnlyList<string> blockedHosts)
    {
        string[] resourceTypes = ["image", "style-sheet", "script", "raw", "media", "svg-document"];
        List<ContentRule> rules = new(blockedHosts.Count);
        foreach (string host in blockedHosts)
        {
            string pattern = $"""^https?://([a-z0-9-]+\.)*{Regex.Escape(host)}([:/]|$)""";
            rules.Add(new ContentRule(new ContentRuleTrigger(pattern, resourceTypes), new ContentRuleAction("block")));
        }

        return JsonSerializer.Serialize(rules);
    }

    private sealed record ContentRule(
        [property: JsonPropertyName("trigger")] ContentRuleTrigger Trigger,
        [property: JsonPropertyName("action")] ContentRuleAction Action);

    private sealed record ContentRuleTrigger(
        [property: JsonPropertyName("url-filter")] string UrlFilter,
        [property: JsonPropertyName("resource-type")] string[] ResourceType);

    private sealed record ContentRuleAction([property: JsonPropertyName("type")] string Type);
}
