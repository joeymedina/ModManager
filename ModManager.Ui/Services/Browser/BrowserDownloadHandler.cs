using Avalonia.Threading;
using ModManager.Ui.ViewModels;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace ModManager.Ui.Services.Browser;

public sealed class BrowserDownloadHandler : DownloadHandler
{
    private readonly Func<BrowsePageViewModel?> _getViewModel;
    private CefDownloadItemCallback? _callback;

    public BrowserDownloadHandler(Func<BrowsePageViewModel?> getViewModel)
    {
        _getViewModel = getViewModel;
    }

    public void Cancel() => _callback?.Cancel();

    protected override void OnBeforeDownload(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        string suggestedName,
        CefBeforeDownloadCallback callback)
    {
        if (_getViewModel() is not { } viewModel ||
            !Uri.TryCreate(downloadItem.Url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        _callback = null;
        string path = viewModel.GetBrowserDownloadPath(uri, suggestedName);
        Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadStarted(Path.GetFileName(path)));
        callback.Continue(path, false);
    }

    protected override void OnDownloadUpdated(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        CefDownloadItemCallback callback)
    {
        _callback = callback;
        double progress = downloadItem.PercentComplete >= 0
            ? downloadItem.PercentComplete / 100d
            : 0;
        bool completed = downloadItem.IsComplete;
        bool canceled = downloadItem.IsCanceled;
        string? path = downloadItem.IsValid && (completed || canceled)
            ? downloadItem.FullPath
            : null;
        Dispatcher.UIThread.Post(() =>
            _getViewModel()?.OnBrowserDownloadUpdated(
                progress,
                completed,
                canceled,
                path));
    }
}
