# macOS ad blocking not working — debugging context

## Why this file exists

This is a context handoff for a fresh Claude Code session running on macOS, picking up where a Windows-only session left off. That session designed and built the entire macOS native ad-blocking/download-interception layer but **could not run or test any of it**, since it was working from a Windows machine. Everything in `Services/Browser/Mac/` compiles cleanly on Windows (P/Invoke declarations don't require the target library to exist at compile time) but its actual runtime behavior on macOS has never been observed. The user reports ad blocking "not working at all" on macOS. One likely root cause has already been found and fixed from Windows (see below) — this session's job is to verify that fix on real hardware and find/fix whatever else is wrong.

**Read this whole file before touching code.** It contains the full architecture, the reasoning behind it, one already-applied fix, and a ranked list of other suspects.

## The app

ModManager is a mod manager for The Sims with an embedded browser (Avalonia 12, `Avalonia.Controls.WebView` — native WebView2 on Windows, native WKWebView on macOS) for browsing mod sites and downloading files. It supports multiple tabs; each tab owns its own native WebView instance.

## Background: why macOS needed custom native interop at all

Avalonia's own cross-platform ad-block-relevant event, `WebResourceRequested`, is raised from `decidePolicyForNavigationAction` on macOS (confirmed by reading `AvaloniaUI/Avalonia.Controls.WebView`'s own source on GitHub) — which WKWebView only calls for *navigations* (link clicks, page loads), never for subresource loads like images/scripts/XHR, which is what ad-blocking rules actually need to match against. So there is no way to build real ad blocking on top of Avalonia's own cross-platform WebView API on macOS. The only real mechanism is WebKit's native `WKContentRuleList` — the same compiled-ruleset API Safari content blockers use — which requires dropping down to raw WebKit/Objective-C calls Avalonia doesn't expose.

Since there's no first-party managed WebKit binding available without pulling in the full `Microsoft.macOS` SDK/workload (which this repo intentionally avoids — it's a single `net10.0` TFM project, no RID-specific or macOS-specific build infrastructure exists anywhere else in the repo), the fix was built as a small, purpose-built Objective-C interop layer using raw `libobjc` P/Invoke — the same technique `Avalonia.Controls.WebView` uses *internally* for its own macOS backend (confirmed by reading `Avalonia.Controls.WebView.Core/Macios/Interop/{Libobjc.cs,BlockLiteral.cs}` on GitHub; those types are `internal` to Avalonia's assembly so they couldn't be reused directly, but they were used as a structural blueprint).

**Do not** try to solve this by pulling in `Microsoft.macOS` / the `net10.0-macos` TFM / Xamarin-style bindings. That would be a much bigger structural change (new workload dependency, multi-targeting the whole `ModManager.Ui` project) than the problem warrants, and it's not the direction this was built in. Stick to the existing raw-P/Invoke pattern unless something about it turns out to be fundamentally broken (unlikely — it mirrors Avalonia's own proven technique).

## Architecture map

```
BrowserTabView (owns one IBrowsePageBrowser per tab)
  -> AvaloniaBrowsePageBrowser
       -> creates AdBlockService() [one instance PER TAB — not shared]
       -> creates MacNativeWebViewPlatformBridge (on macOS)
            -> on WKWebView adapter ready: WKDownloadInterceptor (real downloads)
            -> on WKWebView adapter ready: WKContentRuleListAdBlocker.Apply(...) (real ad blocking)
```

Files, in the order you'll likely need to look at them:

- `ModManager.Ui/Services/AdBlockService.cs` — downloads/caches EasyList (`easylist.to/easylist/easylist.txt`), exposes `GetBlockedHosts()` (list of hostnames extracted from the list) and `IsBlocked(Uri)` (used only by the Windows path). **This is shared logic, not macOS-specific.**
- `ModManager.Ui/Services/Browser/AvaloniaBrowsePageBrowser.cs` — per-tab, creates its own `AdBlockService` instance and kicks off `RefreshAsync()`.
- `ModManager.Ui/Services/Browser/MacNativeWebViewPlatformBridge.cs` — macOS platform bridge. Waits for the WKWebView native adapter to be ready (`AdapterCreated`/`AdapterDestroyed` events, mirroring the same pattern the Windows bridge uses for `CoreWebView2` readiness), then wires up `WKDownloadInterceptor` and calls `WKContentRuleListAdBlocker.Apply`.
- `ModManager.Ui/Services/Browser/Mac/WKContentRuleListAdBlocker.cs` — **the ad-block logic itself.** Builds a WebKit content-blocker JSON ruleset from `AdBlockService.GetBlockedHosts()` (one rule per blocked host), compiles it via `WKContentRuleListStore.default().compileContentRuleList(forIdentifier:encodedContentRuleList:completionHandler:)`, and on success adds the result to the tab's `WKWebView.configuration.userContentController`.
- `ModManager.Ui/Services/Browser/Mac/WKDownloadInterceptor.cs` — real download interception (separate concern from ad blocking, becomes the WKWebView's `navigationDelegate` with message-forwarding to Avalonia's original delegate — see the doc comment at the top of that file for the full reasoning). Not likely related to the ad-block bug, but shares the same interop plumbing.
- `ModManager.Ui/Services/Browser/Mac/Interop/Libobjc.cs`, `BlockLiteral.cs`, `NativeString.cs`, `ManagedObjcClass.cs` — the raw Objective-C runtime plumbing everything else is built on.
- `docs/architecture/browse-page.md` — full architecture doc for the whole browser subsystem (tabs, download tray, Windows side too), for broader context beyond just this bug.
- `ModManager.Tests/Ui/Services/Browser/Mac/WKContentRuleListAdBlockerTests.cs` — existing unit tests for the JSON-generation logic (pure string/JSON, no native calls — these already pass and already caught one real bug: the JSON keys were originally serializing as `Trigger`/`Action` instead of the required lowercase `trigger`/`action`, which would have silently made every rule invalid. That's already fixed. This class of bug — JSON/logic mistakes — is now covered by tests; native/runtime bugs are not, and can't be from Windows.)

## Already fixed: a race condition that plausibly explains "not working at all"

**This may be the entire bug. Verify this first before hunting for anything else.**

`AvaloniaBrowsePageBrowser`'s constructor does:
```csharp
_platformBridge = CreatePlatformBridge(getViewModel);
_platformBridge.Attach(_browser);       // <- Mac bridge tries to apply ad-block rules here
_ = _adBlockService.RefreshAsync();     // <- blocklist download starts here, fire-and-forget
```

`Attach()` → `TryAttachNativeWebView()` fires as soon as the WKWebView native adapter is ready, which happens fast (it's local view construction). `RefreshAsync()` does a real network fetch of EasyList (a multi-MB file) plus parsing — that takes at least hundreds of milliseconds, typically longer. So `WKContentRuleListAdBlocker.Apply()` was being called with `AdBlockService.GetBlockedHosts()` still empty essentially every time, and `Apply()` has an early-out:

```csharp
public static void Apply(IntPtr webViewHandle, IReadOnlyList<string> blockedHosts)
{
    if (blockedHosts.Count == 0)
    {
        return;   // <- silently does nothing, forever, for this tab
    }
    ...
}
```

Unlike Windows (where blocked-host matching happens **per request** via `WebResourceRequested`, so it self-corrects the moment the list finishes loading — the first request or two might slip through, but the 50th won't), macOS's `WKContentRuleList` is install-once: if `Apply()` runs against an empty host list, that tab never blocks anything again, for its entire lifetime, even after the blocklist finishes downloading moments later. This matches "not working at all" exactly, rather than "occasionally missing an ad."

**The fix already applied** (in `MacNativeWebViewPlatformBridge.cs` and `AdBlockService.cs`):
- `AdBlockService.RefreshAsync()` is now single-flight (caches the in-flight `Task` so multiple callers share one fetch instead of each starting a redundant one).
- `MacNativeWebViewPlatformBridge.TryAttachNativeWebView()` now does `await _adBlockService.RefreshAsync()` **before** calling `WKContentRuleListAdBlocker.Apply()`, instead of using whatever snapshot happened to be there.

This was verified to compile on Windows and the existing test suite still passes, but **the actual behavior — does ad blocking now work on a real Mac — has not been verified, because it can't be from Windows.** That's the first thing to check.

One residual, expected (not a bug) limitation even after this fix: the very *first* page load in a brand-new tab can still race ahead of the blocklist download if the tab navigates immediately, so that first load might slip through unfiltered — but any reload or subsequent navigation in that same tab should be filtered correctly, since by then `Apply()` will have already completed and the rule list stays installed on that WebView's `userContentController` for the tab's lifetime. Don't chase this further; it's an acceptable cold-start gap, not the reported bug.

## If the race-condition fix doesn't fully resolve it, check these next (ranked by likelihood)

1. **`compileContentRuleList` is failing silently.** `WKContentRuleListAdBlocker.OnCompiled` currently does:
   ```csharp
   if (contentRuleList == IntPtr.Zero)
   {
       Debug.WriteLine("WKContentRuleList compilation failed.");
       return;
   }
   ```
   It never inspects the `NSError*` it receives. If the generated rule JSON is malformed or uses a `url-filter` regex WebKit's ICU-based rule compiler rejects, this is exactly what would happen: silent, total failure, no crash. **First debugging step**: read the error. Add something like:
   ```csharp
   if (contentRuleList == IntPtr.Zero)
   {
       string? description = error != IntPtr.Zero
           ? NativeString.Read(Libobjc.IntPtr_msgSend(error, Libobjc.sel_getUid("localizedDescription")))
           : null;
       Debug.WriteLine($"WKContentRuleList compilation failed: {description}");
       return;
   }
   ```
   and check Xcode/Console output when running the app. Also worth dumping the actual generated JSON once (`WKContentRuleListAdBlocker.BuildRuleListJson(...)`) and validating it against Apple's documented content-blocker JSON schema by hand for a handful of entries.

2. **`IAppleWKWebViewPlatformHandle`/`handle.WKWebView` isn't returning what's expected.** This type was confirmed to exist via reflection on the compiled `Avalonia.Controls.WebView.dll` from Windows, but its actual runtime value on macOS was never observed. Add a log at the top of `TryAttachNativeWebView()`:
   ```csharp
   Debug.WriteLine($"TryAttachNativeWebView: handle={_browser?.TryGetPlatformHandle()}, webView={webViewHandle}");
   ```
   and confirm it's a real, non-zero pointer, and that `TryAttachNativeWebView()` is actually being *called* (i.e. `AdapterCreated` fires at all — if it never fires, neither ad blocking nor download interception would ever activate, which would also explain "not working at all" and would implicate something upstream of this bug fix entirely).

3. **Objective-C selector or type-encoding mistakes elsewhere in the interop layer.** This is genuinely the first native interop code in this repo (confirmed via repo-wide grep before writing it — no `DllImport`/`LibraryImport` precedent existed anywhere). A wrong type-encoding string in `class_addMethod`, a wrong `objc_msgSend` overload for a given signature, etc. would be easy to get subtly wrong without a way to test locally. This class of bug would more likely *crash* than silently no-op, though, so it's ranked below the two silent-failure hypotheses above — but don't rule it out if you see any native crashes, not just "no blocking."

4. **Timing relative to the tab's own navigation**, covered above under "residual limitation" — only worth chasing if ad blocking still doesn't work on the *second+* page load / reload in a tab, not just the first.

## How to verify the fix (and the feature generally) once on macOS

1. Pull latest, build `ModManager.Ui` for macOS, run it.
2. Open the Browse tab, wait a couple of seconds (let the EasyList fetch complete — it's a real network call), then navigate to a heavily ad-laden site (the shield indicator in the toolbar next to the address bar shows a blocked-ad count on **Windows** — note that on macOS this counter is expected to stay at 0 even when blocking works correctly, since WebKit's content-blocker API doesn't report which URLs it blocked, unlike Windows' per-request event. Don't use the counter as your signal on macOS — use actual visual absence of ads / network tab in Safari Web Inspector attached to the WKWebView, if that's accessible, or just eyeball whether ad iframes/images are present).
3. If ads are still showing: add the `NSError` logging from suspect #1 above, rerun, and read what WebKit actually says about the compiled rule list.
4. Reload the page (or open a second tab) and see if blocking kicks in on the second load even if it didn't on the first — that distinguishes "the whole mechanism is broken" from "just the cold-start race described above."
5. Also sanity-check `WKDownloadInterceptor` while you're in there (real file download, cancel mid-download, and — importantly — confirm normal navigation events like page title/loading state still update, which is the proof that the delegate-forwarding trick didn't silently break Avalonia's own `NavigationCompleted` event). That one hasn't been verified on hardware either, per `docs/architecture/browse-page.md`.

## Constraints / things not to change without a good reason

- Stay on the raw `libobjc` P/Invoke approach (see "Background" above) — don't introduce the macOS workload/typed bindings.
- Keep reusing `AdBlockService.GetBlockedHosts()` as the rule source rather than writing a new EasyList parser — that logic already exists and is shared with the (Windows, working) implementation.
- The single-flight change to `AdBlockService.RefreshAsync()` is intentional and should stay — don't revert it back to plain fire-and-forget.
- `ModManager.Tests/Ui/Services/Browser/Mac/WKContentRuleListAdBlockerTests.cs` covers the pure JSON-generation logic and should keep passing; add to it if you find and fix a rule-format bug there (that's the kind of bug a test can actually catch, unlike the native runtime bugs above).
