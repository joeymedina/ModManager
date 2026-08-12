using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;
using ModManager.Ui.Services.Browser.Mac;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Services.Browser;

[SupportedOSPlatform("macos")]
public sealed class MacNativeWebViewPlatformBridge : INativeWebViewPlatformBridge
{
    private readonly AdBlockService _adBlockService;
    private readonly Func<BrowserTabViewModel?> _getViewModel;
    private readonly Action<Uri> _onNewTabRequested;
    private NativeWebView? _browser;
    private WKDownloadInterceptor? _downloadInterceptor;
    private WKNewWindowInterceptor? _newWindowInterceptor;
    private bool _disposed;

    public MacNativeWebViewPlatformBridge(
        AdBlockService adBlockService,
        Func<BrowserTabViewModel?> getViewModel,
        Action<string> onAdBlocked,
        Action<Uri> onNewTabRequested)
    {
        _adBlockService = adBlockService;
        _getViewModel = getViewModel;
        _onNewTabRequested = onNewTabRequested;
        // WebKit's content-blocker API blocks requests inside its own networking
        // layer and does not report which URLs it blocked, so there is no
        // per-URL signal to raise here on macOS (unlike Windows' CoreWebView2
        // WebResourceRequested). The ad-block shield count only increments on
        // Windows for now.
        _ = onAdBlocked;
    }

    public void Attach(NativeWebView browser)
    {
        _browser = browser;
        browser.AdapterCreated += OnAdapterCreated;
        browser.AdapterDestroyed += OnAdapterDestroyed;
        TryAttachNativeWebView();
    }

    public void CancelDownload()
    {
        _downloadInterceptor?.CancelActiveDownload();
    }

    public void Dispose()
    {
        _disposed = true;
        _downloadInterceptor?.Dispose();
        _downloadInterceptor = null;
        _newWindowInterceptor?.Dispose();
        _newWindowInterceptor = null;

        if (_browser is null)
        {
            return;
        }

        _browser.AdapterCreated -= OnAdapterCreated;
        _browser.AdapterDestroyed -= OnAdapterDestroyed;
        _browser = null;
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        TryAttachNativeWebView();
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        _downloadInterceptor?.Dispose();
        _downloadInterceptor = null;
        _newWindowInterceptor?.Dispose();
        _newWindowInterceptor = null;
    }

    private void TryAttachNativeWebView()
    {
        if (_downloadInterceptor is not null ||
            _browser?.TryGetPlatformHandle() is not IAppleWKWebViewPlatformHandle handle ||
            handle.WKWebView == IntPtr.Zero)
        {
            return;
        }

        IntPtr webViewHandle = handle.WKWebView;
        _downloadInterceptor = new WKDownloadInterceptor(webViewHandle, _getViewModel);
        _newWindowInterceptor = new WKNewWindowInterceptor(webViewHandle, _onNewTabRequested);
        _ = ApplyAdBlockRulesAsync(webViewHandle);
    }

    // AdBlockService.RefreshAsync() is kicked off fire-and-forget alongside
    // tab construction, so GetBlockedHosts() is essentially guaranteed to
    // still be empty the moment the WKWebView adapter first becomes
    // available (native view creation is fast; the EasyList network fetch
    // is not). Unlike Windows' per-request WebResourceRequested check, which
    // self-corrects once the list finishes loading a moment later,
    // WKContentRuleList is a one-shot "compile and install" — applying it
    // once against an empty host list means this tab silently never blocks
    // anything, for its entire lifetime. So: wait for the list before
    // installing the rule list, rather than racing it.
    private async Task ApplyAdBlockRulesAsync(IntPtr webViewHandle)
    {
        await _adBlockService.RefreshAsync();

        if (!_disposed)
        {
            WKContentRuleListAdBlocker.Apply(webViewHandle, _adBlockService.GetBlockedHosts());
        }
    }
}
