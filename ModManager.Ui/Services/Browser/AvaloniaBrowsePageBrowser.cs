using System.Net;
using Avalonia.Controls;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Services.Browser;

public sealed class AvaloniaBrowsePageBrowser : IBrowsePageBrowser
{
    private readonly NativeWebView _browser = new();
    private readonly AdBlockService _adBlockService = new();
    private readonly INativeWebViewPlatformBridge _platformBridge;

    public AvaloniaBrowsePageBrowser(Func<BrowserTabViewModel?> getViewModel)
    {
        _browser.NavigationStarted += OnNavigationStarted;
        _browser.NavigationCompleted += OnNavigationCompleted;
        _platformBridge = CreatePlatformBridge(getViewModel);
        _platformBridge.Attach(_browser);
        _ = _adBlockService.RefreshAsync();
    }

    public Control View => _browser;

    public bool CanGoBack => _browser.CanGoBack;

    public bool CanGoForward => _browser.CanGoForward;

    public event Action? NavigationStarted;

    public event Action<Uri?, bool>? NavigationCompleted;

    public event Action<string>? AdBlocked;

    public event Action<Uri>? NewTabRequested;

    public void Navigate(Uri uri) => _browser.Navigate(uri);

    public void GoBack() => _browser.GoBack();

    public void GoForward() => _browser.GoForward();

    public void Refresh() => _browser.Refresh();

    public async Task<IReadOnlyList<Cookie>> GetCookiesAsync()
    {
        NativeWebViewCookieManager? cookieManager = _browser.TryGetCookieManager();
        return cookieManager is null
            ? Array.Empty<Cookie>()
            : await cookieManager.GetCookiesAsync();
    }

    public void CancelDownload() => _platformBridge.CancelDownload();

    public void Dispose()
    {
        _browser.NavigationStarted -= OnNavigationStarted;
        _browser.NavigationCompleted -= OnNavigationCompleted;
        _platformBridge.Dispose();
        _adBlockService.Dispose();
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        NavigationStarted?.Invoke();
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        NavigationCompleted?.Invoke(_browser.Source, e.IsSuccess);
    }

    private INativeWebViewPlatformBridge CreatePlatformBridge(Func<BrowserTabViewModel?> getViewModel)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsNativeWebViewPlatformBridge(
                _adBlockService,
                getViewModel,
                url => AdBlocked?.Invoke(url),
                uri => NewTabRequested?.Invoke(uri));
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacNativeWebViewPlatformBridge(
                _adBlockService,
                getViewModel,
                url => AdBlocked?.Invoke(url),
                uri => NewTabRequested?.Invoke(uri));
        }

        return new NoopNativeWebViewPlatformBridge();
    }
}
