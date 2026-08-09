# Browse Page Architecture

## Context

The Browse page gives users a tabbed, embedded browser pointed at mod-hosting sites (default: modthesims.info). It supports multiple tabs, direct-URL downloads, browser-initiated downloads surfaced in a shared download tray, native ad blocking, and cookie bridging between the embedded browser session and `HttpClient`-based downloads.

Related documentation: [navigation-shell.md](./navigation-shell.md).

## What Changed (rework)

- Replaced the CefGlue-based browser path entirely — `CefGlue.Avalonia.ARM64` hard-depends on `Avalonia.ReactiveUI 11.0.9` and cannot run on this app's Avalonia 12.1.0. `Avalonia.Controls.WebView` (native WebView2 on Windows, native WKWebView on macOS) is Avalonia 12's own first-party cross-platform WebView and is now the only browser implementation.
- `BrowsePageViewModel` became a thin tab container (`Tabs`, `SelectedTab`, shared `Downloads`); per-tab state moved to a new `BrowserTabViewModel`.
- `BrowsePageView` became the outer shell (toolbar + tab strip); a new `BrowserTabView` owns one `IBrowsePageBrowser` instance per tab.
- Windows: the ad-block resource filter is scoped to the resource contexts EasyList rules actually target instead of `CoreWebView2WebResourceContext.All`, which was routing every request (including the main document, fonts, websockets) through managed-code matching on every navigation.
- macOS: the JS monkey-patch ad blocker and click-detection download sniffing were replaced with native WebKit mechanisms — `WKContentRuleList` for ad blocking and a custom `WKNavigationDelegate`/`WKDownloadDelegate` for real download interception (see below).

## Architecture

```text
┌────────────────────────────────────────────────────────────────────┐
│ BrowsePageView (Avalonia)                                          │
│  Toolbar (bound to SelectedTab), tab strip, download tray flyout   │
└───────────────────────────┬────────────────────────────────────────┘
                            │ DataContext
┌───────────────────────────▼────────────────────────────────────────┐
│ BrowsePageViewModel                                                │
│  Tabs: ObservableCollection<BrowserTabViewModel>, SelectedTab      │
│  Downloads: ObservableCollection<DownloadItemViewModel> (shared)   │
│  Commands: NewTab, SelectTab                                       │
└─────────────┬──────────────────────────────────────────────────────┘
              │ one BrowserTabViewModel per tab
┌─────────────▼──────────────────────────────────────────────────────┐
│ BrowserTabViewModel                                                │
│  Commands: Navigate, GoHome, Close, DownloadCurrent                │
│  Events:   NavigationRequested, CookiesRequested,                  │
│            BrowserDownloadCancellationRequested, CloseRequested    │
│  Callbacks: OnNavigationStarted/Completed, OnAdBlocked,            │
│             OnBrowserDownload{Started/Updated/Failed},              │
│             TryBeginDownload, UpdateNavigationState                │
└─────────────┬──────────────────────────────────────────────────────┘
              │ hosted by
┌─────────────▼──────────────────────────────────────────────────────┐
│ BrowserTabView (Avalonia) — owns one IBrowsePageBrowser            │
└─────────────┬──────────────────────────────────────────────────────┘
              │
┌─────────────▼──────────────────────────────────────────────────────┐
│ IBrowsePageBrowser (AvaloniaBrowsePageBrowser)                     │
│  Wraps Avalonia.Controls.WebView's NativeWebView                   │
└─────────────┬──────────────────────────────────────────────────────┘
              │
    ┌─────────┴──────────┐
    │                     │
┌───▼──────────────────┐ ┌▼─────────────────────────────────────────┐
│ WindowsNativeWebView  │ │ MacNativeWebViewPlatformBridge           │
│ PlatformBridge        │ │  Attaches WKContentRuleListAdBlocker +   │
│  CoreWebView2 native  │ │  WKDownloadInterceptor to the raw        │
│  WebResourceRequested │ │  WKWebView* via raw Objective-C interop  │
│  + DownloadStarting    │ │                                          │
└───────────────────────┘ └──────────────────────────────────────────┘
```

## BrowsePageViewModel / BrowserTabViewModel

`BrowsePageViewModel` is a thin tab container: it owns `Tabs`, `SelectedTab`, and the shared `Downloads` tray, and creates a new `BrowserTabViewModel` (wired with a `BeginDownload` factory callback) for each tab.

`BrowserTabViewModel` carries everything that used to live directly on `BrowsePageViewModel`: address bar state, loading/navigation state, and per-tab ad-block counters (`BlockedAds`/`BlockedAdsTooltip` — page-specific, so these stay per-tab rather than moving to the shared container).

### Download routing

Both browser-initiated downloads (native platform bridge signals a download) and ViewModel-initiated downloads (user clicks Download or navigates to a file URL) now create a `DownloadItemViewModel` via `BrowsePageViewModel.BeginDownload(fileName, sourceUri, cancelRequested)` and add it to the shared `Downloads` collection, instead of tracking a single `IsDownloading`/`DownloadProgress` pair on the page. This lets multiple tabs download concurrently and shows all of them in one tray.

### Download URL Detection

`BrowserDownloadService.LooksLikeDownload(Uri)` is unchanged: absolute HTTP/HTTPS, path ends with a recognized file extension.

## BrowserDownloadService

Unchanged from the original design — a dedicated `HttpClient`-based downloader with progress reporting, `Content-Disposition`-aware filename resolution, and cookie bridging via `ApplyCookies`.

## IBrowsePageBrowser

Unchanged shape; still the abstraction that decouples the tab view/view model from the browser engine. `AvaloniaBrowsePageBrowser` (wrapping `Avalonia.Controls.WebView`) is now the only implementation — the CefGlue implementation and its supporting classes (`CefGlueBrowsePageBrowser`, `AdBlockRequestHandler`, `AdBlockResourceRequestHandler`, `BrowserDownloadHandler`) were deleted, along with the `CefGlue.Avalonia.ARM64` package reference.

## Windows: WindowsNativeWebViewPlatformBridge

Still reaches into the real `CoreWebView2` object (via `TryGetPlatformHandle()` + a private-constructor reflection call, since Avalonia's WebView package exposes no supported managed wrapper for an externally-owned `CoreWebView2`) for native `WebResourceRequested`-based ad blocking and `DownloadStarting`-based download interception.

The one behavioral change: `AddWebResourceRequestedFilter` is now registered per relevant `CoreWebView2WebResourceContext` (`Image`, `Stylesheet`, `Media`, `Script`, `XmlHttpRequest`, `Fetch`, `Other`) instead of `All`. Filtering `All` meant every request of every type — including the main document itself — crossed into managed-code EasyList matching, which was the dominant cause of sluggish page loads.

## macOS: MacNativeWebViewPlatformBridge + native WebKit interop

Two facts about Avalonia's own macOS WebView backend (confirmed by reading its source, `AvaloniaUI/Avalonia.Controls.WebView` on GitHub) drove this rework:

1. Avalonia's cross-platform `WebResourceRequested` event is raised from `decidePolicyForNavigationAction`, which WKWebView only calls for navigations — never for subresource loads (images, scripts, XHR/fetch). It cannot block most ad requests at the network level.
2. Avalonia's macOS adapter implements no download handling at all.

Both gaps are now filled with real WebKit APIs, reached via a narrow, purpose-built Objective-C interop layer (`Services/Browser/Mac/Interop/`) modeled on the same raw-P/Invoke technique Avalonia's own package uses internally for macOS (`Libobjc`, `BlockLiteral`), since there is no first-party managed WebKit binding available outside the full `Microsoft.macOS` SDK/workload.

### WKContentRuleListAdBlocker

Builds a WebKit content-blocker JSON ruleset (one rule per host from the existing `AdBlockService.GetBlockedHosts()` — no new blocklist parsing), compiles it once via `WKContentRuleListStore.default().compileContentRuleList(forIdentifier:encodedContentRuleList:completionHandler:)`, and adds the result to the tab's `WKWebView.configuration.userContentController`. This is the same compiled, WebKit-native mechanism Safari content blockers use, running inside WebKit's networking layer before a request is ever made — no JavaScript injection needed. WebKit does not report which URLs it blocked, so (unlike Windows) the shield's `BlockedAds` count does not increment on macOS; ad blocking itself is real and effective, there's just no per-URL telemetry to surface.

### WKDownloadInterceptor

Becomes the WKWebView's `navigationDelegate` (after capturing Avalonia's original delegate instance) to implement `webView:decidePolicyForNavigationResponse:decisionHandler:` — a selector Avalonia's own delegate does not implement — detecting non-renderable responses (`canShowMIMEType == NO`) and converting them to a `WKDownload`, then acting as that download's `WKDownloadDelegate` for destination/completion/failure/cancel.

Because `navigationDelegate` is a single slot, replacing it would silently stop Avalonia's own `NavigationCompleted` from firing. `WKDownloadInterceptor` avoids this via standard Objective-C message forwarding: it implements `forwardingTargetForSelector:` (returns the captured original delegate for anything it doesn't implement itself) and `respondsToSelector:` (WKNavigationDelegate methods are `@optional`, and the default `respondsToSelector:` does not consult `forwardingTargetForSelector:` on its own, so without this override WebKit would conclude Avalonia's delegate methods aren't implemented and skip calling them entirely).

**Known limitation:** no `WKDownload.progress` KVO is wired up, so macOS downloads show as in-progress with no live percentage, then flip to complete/failed — Windows still reports live percentage via `CoreWebView2`.

**Verification status:** this file's C# compiles on any platform (P/Invoke declarations don't require the target library to exist at compile time) but its actual behavior has only been verified by reading Avalonia's own equivalent implementation, not by running on macOS hardware — it needs manual verification on a Mac (ad blocking on a heavy site, a real file download with cancel-mid-download, and confirming normal navigation/page-load events still fire, which proves the delegate-forwarding didn't break Avalonia's own event pipeline).

## Ad blocking data source: AdBlockService

Unchanged — supplies `IsBlocked(Uri)` (used by the Windows bridge) and `GetBlockedHosts()` (used by both the old Mac JS injection and the new `WKContentRuleListAdBlocker`) from a cached EasyList subscription.

## Dependency Injection

Registered in `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`:

| Type | Lifetime | Note |
| --- | --- | --- |
| `BrowserDownloadService` | Singleton | Owns `HttpClient`; must be disposed on shutdown |
| `BrowsePageViewModel` | Transient | Injected into `MainViewModel` |

`IBrowsePageBrowser` is still **not** registered in DI — each `BrowserTabView` creates and owns its own instance, since it wraps an Avalonia `Control` that must be attached to the visual tree.

## Error and State Behavior

| Condition | Behavior |
| --- | --- |
| Navigation fails | `OnNavigationCompleted(_, false)` sets status message |
| Download canceled | Download item marked `Canceled`, tab status set to "Download canceled." |
| Download fails | `OnBrowserDownloadFailed` / catch marks the download item `Failed` |
| Second download from the same tab while one is active | Status message warns; download is not started |
| Invalid URL in address bar | Status message warns; navigation is not started |
| Closing the last remaining tab | Ignored — at least one tab always stays open |

## Out of Scope

- Download history persistence
- Custom ad-block list configuration UI
- Bookmarks or browsing history
- Automatic installation of downloaded mod archives into the mods folder
- Live download progress percentage on macOS (see Known limitation above)
- Per-URL blocked-ad telemetry on macOS (see WKContentRuleListAdBlocker above)
