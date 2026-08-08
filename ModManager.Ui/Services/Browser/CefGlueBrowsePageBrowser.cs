using System.Net;
using Avalonia.Controls;
using Avalonia.Threading;
using ModManager.Ui.ViewModels;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;

namespace ModManager.Ui.Services.Browser;

public sealed class CefGlueBrowsePageBrowser : IBrowsePageBrowser
{
    private readonly AvaloniaCefBrowser _browser = new();
    private readonly BrowserDownloadHandler _downloadHandler;
    private readonly AdBlockService _adBlockService = new();
    private Uri? _lastAddressUri;

    public CefGlueBrowsePageBrowser(Func<BrowsePageViewModel?> getViewModel)
    {
        _browser.AddressChanged += OnAddressChanged;
        _browser.LoadStart += OnLoadStart;
        _browser.LoadEnd += OnLoadEnd;

        _downloadHandler = new BrowserDownloadHandler(getViewModel);
        _browser.DownloadHandler = _downloadHandler;

        AdBlockRequestHandler adBlockHandler = new(
            _adBlockService,
            url => Dispatcher.UIThread.Post(() => AdBlocked?.Invoke(url)));
        _browser.RequestHandler = adBlockHandler;
        _ = adBlockHandler.RefreshAsync();
    }

    public Control View => _browser;

    public bool CanGoBack => _browser.CanGoBack;

    public bool CanGoForward => _browser.CanGoForward;

    public event Action? NavigationStarted;

    public event Action<Uri?, bool>? NavigationCompleted;

    public event Action<string>? AdBlocked;

    public void Navigate(Uri uri) => _browser.Address = uri.ToString();

    public void GoBack() => _browser.GoBack();

    public void GoForward() => _browser.GoForward();

    public void Refresh() => _browser.Reload();

    public Task<IReadOnlyList<Cookie>> GetCookiesAsync() =>
        Task.FromResult<IReadOnlyList<Cookie>>(Array.Empty<Cookie>());

    public void CancelDownload() => _downloadHandler.Cancel();

    public void Dispose()
    {
        _browser.AddressChanged -= OnAddressChanged;
        _browser.LoadStart -= OnLoadStart;
        _browser.LoadEnd -= OnLoadEnd;
        _adBlockService.Dispose();
    }

    private void OnAddressChanged(object? sender, string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is not "about")
        {
            _lastAddressUri = uri;
        }
    }

    private void OnLoadStart(object? sender, LoadStartEventArgs e)
    {
        if (e.Frame.IsMain)
        {
            NavigationStarted?.Invoke();
        }
    }

    private void OnLoadEnd(object? sender, LoadEndEventArgs e)
    {
        if (!e.Frame.IsMain)
        {
            return;
        }

        NavigationCompleted?.Invoke(
            _lastAddressUri,
            e.HttpStatusCode is >= 200 and < 400);
    }
}
