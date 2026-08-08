using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public partial class BrowsePageViewModel : ViewModelBase
{
    public static readonly Uri DefaultHome = new("https://modthesims.info/");

    private readonly BrowserDownloadService _downloadService;
    private CancellationTokenSource? _downloadCts;
    private bool _browserDownloadActive;

    [ObservableProperty]
    private string _addressText = DefaultHome.ToString();

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string? _lastDownloadPath;

    [ObservableProperty]
    private int _blockedAds;

    [ObservableProperty]
    private string _blockedAdsTooltip = "No ads blocked";

    private readonly HashSet<string> _blockedAdUrls = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<Uri>? NavigationRequested;

    public event Func<Task<IReadOnlyList<Cookie>>>? CookiesRequested;
    public event Action? BrowserDownloadCancellationRequested;

    public BrowsePageViewModel()
        : this(new BrowserDownloadService())
    {
    }

    public BrowsePageViewModel(BrowserDownloadService downloadService)
    {
        _downloadService = downloadService;
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

    private bool CanDownloadCurrent() => !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload()
    {
        if (_browserDownloadActive)
        {
            BrowserDownloadCancellationRequested?.Invoke();
        }
        else
        {
            _downloadCts?.Cancel();
        }
    }

    private bool CanCancelDownload() => IsDownloading;

    public string GetBrowserDownloadPath(Uri uri, string? suggestedFileName) =>
        _downloadService.GetDownloadPath(uri, suggestedFileName);

    public void OnBrowserDownloadStarted(string fileName)
    {
        _browserDownloadActive = true;
        IsDownloading = true;
        IsLoading = false;
        DownloadProgress = 0;
        StatusMessage = $"Downloading {fileName}...";
        DownloadCurrentCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
    }

    public void OnBrowserDownloadUpdated(double progress, bool completed, bool canceled, string? path)
    {
        DownloadProgress = progress;
        if (completed)
        {
            _browserDownloadActive = false;
            IsDownloading = false;
            LastDownloadPath = path;
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            DownloadCurrentCommand.NotifyCanExecuteChanged();
            CancelDownloadCommand.NotifyCanExecuteChanged();
        }
        else if (canceled)
        {
            _browserDownloadActive = false;
            IsDownloading = false;
            StatusMessage = "Download canceled.";
            DownloadCurrentCommand.NotifyCanExecuteChanged();
            CancelDownloadCommand.NotifyCanExecuteChanged();
        }
        else
        {
            StatusMessage = $"Downloading... {progress:P0}";
        }
    }

    public void OnBrowserDownloadFailed(string message)
    {
        _browserDownloadActive = false;
        IsDownloading = false;
        StatusMessage = message;
        DownloadCurrentCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
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
        if (IsDownloading)
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
        if (IsDownloading)
        {
            return;
        }

        IsLoading = false;

        if (topLevelSource is not null)
        {
            AddressText = topLevelSource.ToString();
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
        if (IsDownloading)
        {
            StatusMessage = "A download is already in progress.";
            return;
        }

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _downloadCts.Token;

        IsDownloading = true;
        IsLoading = false;
        DownloadProgress = 0;
        StatusMessage = $"Downloading {uri.Segments.LastOrDefault() ?? uri.Host}...";
        DownloadCurrentCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();

        try
        {
            await ApplyWebViewCookiesAsync();

            Progress<double> progress = new(value =>
            {
                DownloadProgress = value;
                StatusMessage = $"Downloading... {value:P0}";
            });

            BrowserDownloadResult result = await _downloadService.DownloadAsync(
                uri,
                progress: progress,
                cancellationToken: cancellationToken);

            LastDownloadPath = result.FilePath;
            DownloadProgress = 1;
            StatusMessage = $"Saved {Path.GetFileName(result.FilePath)} ({FormatBytes(result.BytesWritten)})";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadCurrentCommand.NotifyCanExecuteChanged();
            CancelDownloadCommand.NotifyCanExecuteChanged();
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
        }
        catch
        {
            // Cookies are best-effort for authenticated downloads.
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
