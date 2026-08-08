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
    private NativeWebView? _browser;
    private WKDownloadInterceptor? _downloadInterceptor;

    public MacNativeWebViewPlatformBridge(
        AdBlockService adBlockService,
        Func<BrowserTabViewModel?> getViewModel,
        Action<string> onAdBlocked)
    {
        _adBlockService = adBlockService;
        _getViewModel = getViewModel;
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
        _downloadInterceptor?.Dispose();
        _downloadInterceptor = null;

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
        WKContentRuleListAdBlocker.Apply(webViewHandle, _adBlockService.GetBlockedHosts());
    }
}
