using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace ModManager.Ui.Services;

public sealed class BrowserDownloadService : IDisposable
{
    private static readonly HashSet<string> DownloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz",
        ".package", ".ts4script", ".sims3pack", ".trayitem", ".blueprint", ".bpi",
        ".exe", ".msi", ".dmg", ".pkg", ".deb", ".rpm", ".appimage",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp3", ".mp4", ".mov", ".avi",
        ".json", ".xml", ".yaml", ".yml"
    };

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer = new();
    private bool _disposed;

    public BrowserDownloadService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) ModManager/1.0");
    }

    public static string DefaultDownloadFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public static bool LooksLikeDownload(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        string path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path is "/")
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return DownloadExtensions.Contains(extension);
    }

    public string GetDownloadPath(Uri uri, string? suggestedFileName = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Directory.CreateDirectory(DefaultDownloadFolder);
        using StringContent content = new(string.Empty);
        string fileName = ResolveFileName(uri, content.Headers, suggestedFileName);
        return GetUniquePath(Path.Combine(DefaultDownloadFolder, fileName));
    }

    public void ApplyCookies(IEnumerable<Cookie> cookies)
    {
        foreach (Cookie cookie in cookies)
        {
            try
            {
                _cookieContainer.Add(cookie);
            }
            catch
            {
                // Ignore malformed cookies from the webview.
            }
        }
    }

    public async Task<BrowserDownloadResult> DownloadAsync(
        Uri uri,
        string? destinationFolder = null,
        string? suggestedFileName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string folder = string.IsNullOrWhiteSpace(destinationFolder)
            ? DefaultDownloadFolder
            : destinationFolder;

        Directory.CreateDirectory(folder);

        using HttpResponseMessage response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        string fileName = ResolveFileName(uri, response.Content.Headers, suggestedFileName);
        string destinationPath = GetUniquePath(Path.Combine(folder, fileName));

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;

            if (totalBytes is > 0)
            {
                progress?.Report(Math.Clamp(written / (double)totalBytes.Value, 0, 1));
            }
        }

        progress?.Report(1);
        return new BrowserDownloadResult(destinationPath, written);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private static string ResolveFileName(
        Uri uri,
        HttpContentHeaders headers,
        string? suggestedFileName)
    {
        if (!string.IsNullOrWhiteSpace(suggestedFileName))
        {
            return SanitizeFileName(suggestedFileName);
        }

        string? fromHeader = GetFileNameFromContentDisposition(headers.ContentDisposition);
        if (!string.IsNullOrWhiteSpace(fromHeader))
        {
            return SanitizeFileName(fromHeader);
        }

        string fromUri = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(fromUri))
        {
            return SanitizeFileName(Uri.UnescapeDataString(fromUri));
        }

        return $"download-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string? GetFileNameFromContentDisposition(ContentDispositionHeaderValue? contentDisposition)
    {
        if (contentDisposition is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contentDisposition.FileNameStar))
        {
            return contentDisposition.FileNameStar.Trim('"');
        }

        if (!string.IsNullOrWhiteSpace(contentDisposition.FileName))
        {
            return contentDisposition.FileName.Trim('"');
        }

        return null;
    }

    private static string SanitizeFileName(string fileName)
    {
        string name = fileName.Replace('\\', '/');
        name = name.Split('/').LastOrDefault() ?? fileName;
        name = Regex.Replace(name, @"[<>:""|?*\x00-\x1F]", "_");
        name = name.Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(name)
            ? $"download-{DateTime.Now:yyyyMMdd-HHmmss}"
            : name;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string? directory = Path.GetDirectoryName(path) ?? DefaultDownloadFolder;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int i = 1; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}

public sealed record BrowserDownloadResult(string FilePath, long BytesWritten);
