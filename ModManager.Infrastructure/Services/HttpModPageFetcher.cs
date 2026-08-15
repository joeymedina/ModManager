using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// Plain, session-less HTTP <see cref="IModPageFetcher"/>. Covers any strategy whose
/// <see cref="SiteCapabilities.RequiresAuthenticatedSession"/> is false — Sacrificial today. A site
/// that returns a login wall or a challenge page to a cookieless client needs a different, WebView-
/// backed implementation registered from the UI composition root; this one is never routed to those.
/// </summary>
public sealed class HttpModPageFetcher(ILogger<HttpModPageFetcher>? logger = null) : IModPageFetcher
{
    private readonly HttpClient _httpClient = CreateClient();
    private readonly ILogger<HttpModPageFetcher> _logger = logger ?? NullLogger<HttpModPageFetcher>.Instance;

    public async Task<PageContent> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogDebug("Fetched {Url} ({Length} chars)", url, html.Length);
        return new PageContent(response.RequestMessage?.RequestUri ?? url, html);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModManager", "1.0"));
        return client;
    }
}
