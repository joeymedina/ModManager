using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsReleaseClient(ILogger<WickedWhimsReleaseClient>? logger = null)
{
    private const string DownloadPage = "https://wickedwhimsmod.com/download/";
    internal const string ItchPage = "https://turbodriver.itch.io/wickedwhims";

    private readonly HttpClient httpClient = CreateClient();
    private readonly ILogger<WickedWhimsReleaseClient> _logger = logger ?? NullLogger<WickedWhimsReleaseClient>.Instance;

    /// <summary>
    /// Gets latest release metadata for WickedWhims.
    /// </summary>
    public async Task<ModReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        string html = await httpClient.GetStringAsync(DownloadPage, cancellationToken);
        _logger.LogDebug("Fetched {DownloadPage} ({Length} chars)", DownloadPage, html.Length);

        Match heading = Regex.Match(html, @"WickedWhims\s+v(\d+[a-z]?)", RegexOptions.IgnoreCase);
        if (!heading.Success)
        {
            // Scraped from someone else's HTML — when this breaks it's usually a page redesign.
            _logger.LogWarning("No version heading matched on {DownloadPage}; the page layout has probably changed", DownloadPage);
            throw new InvalidOperationException("The official page did not contain a WickedWhims release version.");
        }

        Match date = Regex.Match(html, @"([A-Z][a-z]+\s+\d{1,2}(?:st|nd|rd|th),\s+\d{4})");
        _logger.LogDebug("Matched latest release v{Version}, date {ReleaseDate}", heading.Groups[1].Value, date.Success ? date.Value : "unmatched");

        return new ModReleaseInfo(heading.Groups[1].Value.ToLowerInvariant(), date.Success ? date.Value : null);
    }

    /// <summary>
    /// Downloads the latest WickedWhims archive from the official service, along with the resolved
    /// file URL actually used (itch.io signs it per-request, so it's only known after resolving).
    /// </summary>
    public async Task<WickedWhimsDownload> DownloadLatestArchiveAsync(CancellationToken cancellationToken)
    {
        string page = await httpClient.GetStringAsync(ItchPage, cancellationToken);
        _logger.LogDebug("Fetched {ItchPage} ({Length} chars)", ItchPage, page.Length);

        Match upload = Regex.Match(page, @"data-upload_id=""(\d+)""[^>]*>[\s\S]*?<strong[^>]*class=""name""[^>]*>([^<]+)", RegexOptions.IgnoreCase);
        Match csrf = Regex.Match(page, @"<meta\s+name=""csrf_token""\s+value=""([^""]+)", RegexOptions.IgnoreCase);

        if (!upload.Success || !csrf.Success)
        {
            _logger.LogWarning(
                "Could not resolve the itch.io download on {ItchPage} (upload matched: {UploadMatched}, csrf matched: {CsrfMatched}); the page layout has probably changed",
                ItchPage,
                upload.Success,
                csrf.Success);
            throw new InvalidOperationException("Could not resolve the official download archive.");
        }

        _logger.LogDebug("Resolved itch.io upload {UploadId} (\"{UploadName}\")", upload.Groups[1].Value, upload.Groups[2].Value.Trim());

        using FormUrlEncodedContent form = new(new Dictionary<string, string> { ["csrf_token"] = csrf.Groups[1].Value });
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"{ItchPage}/file/{upload.Groups[1].Value}?source=game_download&after_download_lightbox=1&as_props=1",
            form,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Official download service returned an invalid response.");
        }

        string? url = payload.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Official download service returned a payload with no archive URL");
            throw new InvalidOperationException("Official download service returned no archive URL.");
        }

        byte[] bytes = await httpClient.GetByteArrayAsync(url, cancellationToken);

        // itch.io signs the URL per request — log where it pointed, never the signed query string.
        _logger.LogDebug(
            "Downloaded {ByteCount} bytes from {DownloadHost}{DownloadPath}",
            bytes.Length,
            Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Host : "unparsable",
            parsed?.AbsolutePath ?? string.Empty);

        return new WickedWhimsDownload(url, bytes);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModManager", "1.0"));
        return client;
    }
}

internal sealed record WickedWhimsDownload(string Url, byte[] Bytes);
