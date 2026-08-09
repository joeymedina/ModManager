using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsReleaseClient
{
    private const string DownloadPage = "https://wickedwhimsmod.com/download/";
    internal const string ItchPage = "https://turbodriver.itch.io/wickedwhims";

    private readonly HttpClient httpClient = CreateClient();

    /// <summary>
    /// Gets latest release metadata for WickedWhims.
    /// </summary>
    public async Task<ModReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        string html = await httpClient.GetStringAsync(DownloadPage, cancellationToken);
        Match heading = Regex.Match(html, @"WickedWhims\s+v(\d+[a-z]?)", RegexOptions.IgnoreCase);
        if (!heading.Success)
        {
            throw new InvalidOperationException("The official page did not contain a WickedWhims release version.");
        }

        Match date = Regex.Match(html, @"([A-Z][a-z]+\s+\d{1,2}(?:st|nd|rd|th),\s+\d{4})");
        return new ModReleaseInfo(heading.Groups[1].Value.ToLowerInvariant(), date.Success ? date.Value : null);
    }

    /// <summary>
    /// Downloads the latest WickedWhims archive from the official service, along with the resolved
    /// file URL actually used (itch.io signs it per-request, so it's only known after resolving).
    /// </summary>
    public async Task<WickedWhimsDownload> DownloadLatestArchiveAsync(CancellationToken cancellationToken)
    {
        string page = await httpClient.GetStringAsync(ItchPage, cancellationToken);
        Match upload = Regex.Match(page, @"data-upload_id=""(\d+)""[^>]*>[\s\S]*?<strong[^>]*class=""name""[^>]*>([^<]+)", RegexOptions.IgnoreCase);
        Match csrf = Regex.Match(page, @"<meta\s+name=""csrf_token""\s+value=""([^""]+)", RegexOptions.IgnoreCase);

        if (!upload.Success || !csrf.Success)
        {
            throw new InvalidOperationException("Could not resolve the official download archive.");
        }

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
            throw new InvalidOperationException("Official download service returned no archive URL.");
        }

        byte[] bytes = await httpClient.GetByteArrayAsync(url, cancellationToken);
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
