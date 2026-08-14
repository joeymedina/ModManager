using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

/// <summary>
/// Fetches a page for a site strategy to parse. The HTTP implementation covers sites like Sacrificial
/// that need no session; a strategy that declares <see cref="SiteCapabilities.RequiresAuthenticatedSession"/>
/// is routed to a WebView-backed implementation instead, registered from the UI composition root.
/// </summary>
public interface IModPageFetcher
{
    Task<PageContent> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}
