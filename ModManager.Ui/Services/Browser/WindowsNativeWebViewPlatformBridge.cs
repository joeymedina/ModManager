using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Services.Browser;

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeWebViewPlatformBridge : INativeWebViewPlatformBridge
{
    // Only the contexts EasyList-style rules actually target. Filtering
    // CoreWebView2WebResourceContext.All routes every request (including the
    // main document, fonts, websockets, etc.) through managed-code matching
    // on every navigation, which is the dominant cause of sluggish page loads.
    internal static readonly CoreWebView2WebResourceContext[] AdBlockResourceContexts =
    [
        CoreWebView2WebResourceContext.Image,
        CoreWebView2WebResourceContext.Stylesheet,
        CoreWebView2WebResourceContext.Media,
        CoreWebView2WebResourceContext.Script,
        CoreWebView2WebResourceContext.XmlHttpRequest,
        CoreWebView2WebResourceContext.Fetch,
        CoreWebView2WebResourceContext.Other,
    ];

    private readonly AdBlockService _adBlockService;
    private readonly Func<BrowserTabViewModel?> _getViewModel;
    private readonly Action<string> _onAdBlocked;
    private readonly Action<Uri> _onNewTabRequested;
    private NativeWebView? _browser;
    private CoreWebView2? _coreWebView;
    private CoreWebView2DownloadOperation? _activeDownloadOperation;

    public WindowsNativeWebViewPlatformBridge(
        AdBlockService adBlockService,
        Func<BrowserTabViewModel?> getViewModel,
        Action<string> onAdBlocked,
        Action<Uri> onNewTabRequested)
    {
        _adBlockService = adBlockService;
        _getViewModel = getViewModel;
        _onAdBlocked = onAdBlocked;
        _onNewTabRequested = onNewTabRequested;
    }

    public void Attach(NativeWebView browser)
    {
        _browser = browser;
        browser.AdapterCreated += OnAdapterCreated;
        browser.AdapterDestroyed += OnAdapterDestroyed;
        TryAttachCoreWebView();
    }

    public void CancelDownload()
    {
        _activeDownloadOperation?.Cancel();
    }

    public void Dispose()
    {
        DetachActiveDownload();

        if (_coreWebView is not null)
        {
            _coreWebView.WebResourceRequested -= OnWebResourceRequested;
            _coreWebView.DownloadStarting -= OnDownloadStarting;
            _coreWebView.NewWindowRequested -= OnNewWindowRequested;
            _coreWebView = null;
        }

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
        TryAttachCoreWebView();
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        DetachActiveDownload();
        _coreWebView = null;
    }

    private void TryAttachCoreWebView()
    {
        if (_coreWebView is not null ||
            _browser?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle ||
            handle.CoreWebView2 == IntPtr.Zero)
        {
            return;
        }

        object nativeCore = Marshal.GetObjectForIUnknown(handle.CoreWebView2);
        _coreWebView = (CoreWebView2)Activator.CreateInstance(
            typeof(CoreWebView2),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [nativeCore],
            culture: null)!;
        foreach (CoreWebView2WebResourceContext context in AdBlockResourceContexts)
        {
            _coreWebView.AddWebResourceRequestedFilter("*", context);
        }

        _coreWebView.WebResourceRequested += OnWebResourceRequested;
        _coreWebView.DownloadStarting += OnDownloadStarting;
        _coreWebView.NewWindowRequested += OnNewWindowRequested;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_coreWebView is null ||
            !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri? uri) ||
            !_adBlockService.IsBlocked(uri))
        {
            return;
        }

        _onAdBlocked(uri.ToString());
        e.Response = _coreWebView.Environment.CreateWebResourceResponse(
            new MemoryStream(),
            204,
            "No Content",
            "Content-Length: 0");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        // Handled=true with no NewWindow assigned tells WebView2 not to open anything itself;
        // the target URL is opened as a new app tab instead via _onNewTabRequested.
        e.Handled = true;
        Dispatcher.UIThread.Post(() => _onNewTabRequested(uri));
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_getViewModel() is not { } viewModel ||
            !Uri.TryCreate(e.DownloadOperation.Uri, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        string? suggestedFileName = Path.GetFileName(e.ResultFilePath);
        string path = viewModel.GetBrowserDownloadPath(uri, suggestedFileName);
        e.Handled = true;
        e.ResultFilePath = path;

        Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadStarted(Path.GetFileName(path), uri));

        DetachActiveDownload();
        _activeDownloadOperation = e.DownloadOperation;
        _activeDownloadOperation.BytesReceivedChanged += OnDownloadProgressChanged;
        _activeDownloadOperation.StateChanged += OnDownloadStateChanged;
    }

    private void OnDownloadProgressChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2DownloadOperation downloadOperation)
        {
            return;
        }

        double progress = downloadOperation.TotalBytesToReceive is > 0
            ? Math.Clamp(downloadOperation.BytesReceived / (double)downloadOperation.TotalBytesToReceive.Value, 0, 1)
            : 0;

        Dispatcher.UIThread.Post(() =>
            _getViewModel()?.OnBrowserDownloadUpdated(
                progress,
                completed: false,
                canceled: false,
                path: null));
    }

    private void OnDownloadStateChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2DownloadOperation downloadOperation)
        {
            return;
        }

        BrowserTabViewModel? viewModel = _getViewModel();
        if (viewModel is null)
        {
            return;
        }

        switch (downloadOperation.State)
        {
            case CoreWebView2DownloadState.InProgress:
                OnDownloadProgressChanged(sender, e);
                break;
            case CoreWebView2DownloadState.Completed:
                Dispatcher.UIThread.Post(() =>
                    viewModel.OnBrowserDownloadUpdated(
                        progress: 1,
                        completed: true,
                        canceled: false,
                        path: downloadOperation.ResultFilePath));
                DetachActiveDownload();
                break;
            case CoreWebView2DownloadState.Interrupted:
                Dispatcher.UIThread.Post(() =>
                    viewModel.OnBrowserDownloadFailed(
                        $"Download failed: {downloadOperation.InterruptReason}"));
                DetachActiveDownload();
                break;
        }
    }

    private void DetachActiveDownload()
    {
        if (_activeDownloadOperation is null)
        {
            return;
        }

        _activeDownloadOperation.BytesReceivedChanged -= OnDownloadProgressChanged;
        _activeDownloadOperation.StateChanged -= OnDownloadStateChanged;
        _activeDownloadOperation = null;
    }
}
