using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public partial class BrowserTabViewModel : ViewModelBase
{
    public static readonly Uri DefaultHome = new("https://modthesims.info/");

    private readonly BrowserDownloadService _downloadService;
    private readonly Func<string, Uri?, Action, DownloadItemViewModel> _beginDownload;
    private readonly ILogger<BrowserTabViewModel> _logger;
    private CancellationTokenSource? _downloadCts;
    private DownloadItemViewModel? _activeDownload;

    [ObservableProperty]
    private string _addressText = DefaultHome.ToString();

    [ObservableProperty]
    private string _title = "New Tab";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _blockedAds;

    [ObservableProperty]
    private string _blockedAdsTooltip = "No ads blocked";

    private readonly HashSet<string> _blockedAdUrls = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<Uri>? NavigationRequested;

    public event Func<Task<IReadOnlyList<Cookie>>>? CookiesRequested;
    public event Action? BrowserDownloadCancellationRequested;
    public event Action<BrowserTabViewModel>? CloseRequested;

    public BrowserTabViewModel()
        : this(new BrowserDownloadService(), static (fileName, _, cancel) => new DownloadItemViewModel(fileName, null, cancel))
    {
    }

    public BrowserTabViewModel(
        BrowserDownloadService downloadService,
        Func<string, Uri?, Action, DownloadItemViewModel> beginDownload,
        ILogger<BrowserTabViewModel>? logger = null)
    {
        _downloadService = downloadService;
        _beginDownload = beginDownload;
        _logger = logger ?? NullLogger<BrowserTabViewModel>.Instance;
    }

    [RelayCommand]
    private void Navigate()
    {
        if (!TryCreateUri(AddressText, out Uri? uri) || uri is null)
        {
            StatusMessage = "Enter a valid URL.";
            return;
        }

        if (BrowserDownloadService.LooksLikeDownload(uri))
        {
            _ = DownloadAsync(uri);
            return;
        }

        RequestNavigation(uri);
    }

    [RelayCommand]
    private void GoHome()
    {
        RequestNavigation(DefaultHome);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanDownloadCurrent))]
    private async Task DownloadCurrentAsync()
    {
        if (!TryCreateUri(AddressText, out Uri? uri) || uri is null)
        {
            StatusMessage = "Enter a valid URL to download.";
            return;
        }

        await DownloadAsync(uri);
    }

    private bool CanDownloadCurrent() => _activeDownload is null;

    public string GetBrowserDownloadPath(Uri uri, string? suggestedFileName) =>
        _downloadService.GetDownloadPath(uri, suggestedFileName);

    public void OnBrowserDownloadStarted(string fileName, Uri? sourceUri = null)
    {
        IsLoading = false;
        StatusMessage = $"Downloading {fileName}...";
        _activeDownload = _beginDownload(fileName, sourceUri, () => BrowserDownloadCancellationRequested?.Invoke());

        // The download is intercepted before the tab navigates anywhere, so AddressText is still
        // whatever page the user clicked the download link on — the closest thing to "the mod's
        // page" this flow has. DownloadAsync's own direct-link fallback never sets this: there,
        // the address bar's URL and the download's URL are the same thing, not a separate page.
        if (TryCreateUri(AddressText, out Uri? modPageUri))
        {
            _activeDownload.ModPageUri = modPageUri;
        }

        DownloadCurrentCommand.NotifyCanExecuteChanged();
    }

    public void OnBrowserDownloadUpdated(double progress, bool completed, bool canceled, string? path)
    {
        if (_activeDownload is null)
        {
            return;
        }

        _activeDownload.Progress = progress;
        if (completed)
        {
            _activeDownload.MarkCompleted(path);
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            _activeDownload = null;
            DownloadCurrentCommand.NotifyCanExecuteChanged();
        }
        else if (canceled)
        {
            _activeDownload.MarkCanceled();
            StatusMessage = "Download canceled.";
            _activeDownload = null;
            DownloadCurrentCommand.NotifyCanExecuteChanged();
        }
        else
        {
            StatusMessage = $"Downloading... {progress:P0}";
        }
    }

    public void OnBrowserDownloadFailed(string message)
    {
        _activeDownload?.MarkFailed(message);
        _activeDownload = null;
        StatusMessage = message;
        DownloadCurrentCommand.NotifyCanExecuteChanged();
    }

    public bool TryBeginDownload(Uri? uri)
    {
        if (!BrowserDownloadService.LooksLikeDownload(uri) || uri is null)
        {
            return false;
        }

        _ = DownloadAsync(uri);
        return true;
    }

    public void OnNavigationStarted()
    {
        if (_activeDownload is not null)
        {
            return;
        }

        BlockedAds = 0;
        _blockedAdUrls.Clear();
        BlockedAdsTooltip = "No ads blocked";
        IsLoading = true;
        StatusMessage = "Loading...";
    }

    public void OnAdBlocked(string url)
    {
        if (!_blockedAdUrls.Add(url))
        {
            return;
        }

        BlockedAds = _blockedAdUrls.Count;
        StringBuilder tooltip = new("Blocked ads:");
        foreach (string blockedUrl in _blockedAdUrls)
        {
            tooltip.AppendLine();
            tooltip.Append(blockedUrl);
        }

        BlockedAdsTooltip = tooltip.ToString();
    }

    public void OnNavigationCompleted(Uri? topLevelSource, bool isSuccess)
    {
        if (_activeDownload is not null)
        {
            return;
        }

        IsLoading = false;

        if (topLevelSource is not null)
        {
            AddressText = topLevelSource.ToString();
            Title = topLevelSource.Host;
        }

        StatusMessage = isSuccess
            ? topLevelSource?.Host ?? "Done."
            : "Navigation failed.";
    }

    public void UpdateNavigationState(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    private void RequestNavigation(Uri uri)
    {
        AddressText = uri.ToString();
        IsLoading = true;
        StatusMessage = $"Loading {uri.Host}...";
        NavigationRequested?.Invoke(this, uri);
    }

    private async Task DownloadAsync(Uri uri)
    {
        if (_activeDownload is not null)
        {
            StatusMessage = "A download is already in progress.";
            return;
        }

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _downloadCts.Token;

        IsLoading = false;
        string fileName = uri.Segments.LastOrDefault() ?? uri.Host;
        StatusMessage = $"Downloading {fileName}...";
        DownloadItemViewModel download = _beginDownload(fileName, uri, () => _downloadCts?.Cancel());
        _activeDownload = download;
        DownloadCurrentCommand.NotifyCanExecuteChanged();

        try
        {
            await ApplyWebViewCookiesAsync();

            Progress<double> progress = new(value =>
            {
                download.Progress = value;
                StatusMessage = $"Downloading... {value:P0}";
            });

            BrowserDownloadResult result = await _downloadService.DownloadAsync(
                uri,
                progress: progress,
                cancellationToken: cancellationToken);

            download.MarkCompleted(result.FilePath);
            StatusMessage = $"Saved {Path.GetFileName(result.FilePath)} ({FormatBytes(result.BytesWritten)})";
        }
        catch (OperationCanceledException)
        {
            download.MarkCanceled();
            StatusMessage = "Download canceled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download from {DownloadUri} failed", uri);
            download.MarkFailed(ex.Message);
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            _activeDownload = null;
            DownloadCurrentCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ApplyWebViewCookiesAsync()
    {
        if (CookiesRequested is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<Cookie> cookies = await CookiesRequested.Invoke();
            _downloadService.ApplyCookies(cookies);
            // Count only — cookie values are live session credentials and must never be logged.
            _logger.LogDebug("Applied {CookieCount} webview cookie(s) to the download client", cookies.Count);
        }
        catch (Exception ex)
        {
            // Cookies are best-effort for authenticated downloads.
            _logger.LogDebug(ex, "Could not apply webview cookies; an authenticated download may fail");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private static bool TryCreateUri(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string input = text.Trim();
        if (input.Contains(" ", StringComparison.Ordinal))
        {
            uri = CreateSearchUri(input);
            return true;
        }

        string url = input.Contains("://", StringComparison.Ordinal)
            ? input
            : "https://" + input;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            uri = CreateSearchUri(input);
            return true;
        }

        if (!input.Contains("://", StringComparison.Ordinal) &&
            !input.Contains('.', StringComparison.Ordinal) &&
            !input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            uri = CreateSearchUri(input);
            return true;
        }

        uri = parsed;
        return true;
    }

    private static Uri CreateSearchUri(string query) =>
        new($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
}
