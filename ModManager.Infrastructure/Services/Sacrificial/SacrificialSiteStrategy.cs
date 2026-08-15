using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services.Sacrificial;

/// <summary>
/// Site strategy for sacrificialmods.com. The entire site's mod list lives on one static page,
/// downloads.html, with no auth and no Cloudflare interstitial — so a full check is one HTTP request
/// regardless of how many Sacrificial mods are installed, and <see cref="TryResolveModKey"/> never
/// needs to fetch anything: the mod key is the URL fragment the site itself uses as each mod's
/// permalink (<c>id="ZombieApocalypseDownload"</c> on its card, matching the exact fragment the site's
/// own "copy mod link" button produces), used unmodified rather than transformed — no delimiter
/// stripping needed since the id is already the mod's canonical identity on the site.
/// </summary>
public sealed class SacrificialSiteStrategy(IModPageFetcher fetcher, ILogger<SacrificialSiteStrategy>? logger = null) : IModSiteStrategy
{
    public const string StrategySiteKey = "sacrificialmods.com";

    internal static readonly Uri DownloadsPageUrl = new("https://sacrificialmods.com/downloads.html");

    // Each mod's card is a flat, sibling-less <div class="mod-card" id="...">; card boundaries are
    // found by locating consecutive start tags rather than balancing nested </div>s, which a regex
    // can't do reliably. See ParseModCards.
    private static readonly Regex ModCardStart = new(@"<div class=""mod-card""\s+id=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex VersionBadge = new(@"<span class=""version-badge"">\s*([^<]*?)\s*</span>", RegexOptions.Compiled);
    private static readonly Regex LastUpdate = new(@"Last Update:\s*<span[^>]*>\s*<B>\s*([^<]*?)\s*</B>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SearchTitle = new(@"data-search-title=""([^""]*)""", RegexOptions.Compiled);
    // btn-primary is specifically SacrificialMods.com's own direct download — cards also carry a
    // btn-patreon alternative and several btn-secondary links (release notes, video) that must not
    // be picked up here.
    private static readonly Regex PrimaryDownloadLink = new(@"<a href=""([^""]+)""\s+class=""btn btn-primary""", RegexOptions.Compiled);

    private readonly ILogger<SacrificialSiteStrategy> _logger = logger ?? NullLogger<SacrificialSiteStrategy>.Instance;

    public string SiteKey => StrategySiteKey;

    public IReadOnlyList<string> Hosts { get; } = ["sacrificialmods.com", "www.sacrificialmods.com"];

    public SiteCapabilities Capabilities { get; } = new(RequiresAuthenticatedSession: false, ProvidesUpdatedOnDate: true);

    public SiteModKey? TryResolveModKey(ModKeyHints hints)
    {
        ArgumentNullException.ThrowIfNull(hints);

        string? fragment = GetFragment(hints.ModPageUrl);
        return string.IsNullOrWhiteSpace(fragment) ? null : new SiteModKey(fragment);
    }

    public async Task<IReadOnlyList<SiteObservation>> FetchObservationsAsync(IReadOnlyList<SiteModKey> modKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modKeys);

        if (modKeys.Count == 0)
        {
            return [];
        }

        PageContent page = await fetcher.FetchAsync(DownloadsPageUrl, cancellationToken);
        IReadOnlyList<SiteObservation> parsed = ParseModCards(page.Html);

        HashSet<string> requestedKeys = new(modKeys.Select(key => key.Value), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<SiteObservation> matched = [.. parsed.Where(observation => requestedKeys.Contains(observation.ModKey.Value))];

        _logger.LogInformation(
            "Parsed {ParsedCount} mod card(s) from {Url}, {MatchedCount} matched {RequestedCount} requested key(s)",
            parsed.Count,
            page.Url,
            matched.Count,
            modKeys.Count);

        return matched;
    }

    /// <summary>
    /// Parses every mod card on the page, regardless of which keys were requested — filtering to the
    /// requested set happens in the caller. Public (not private) so tests can assert the parse in
    /// isolation from the requested-key filter — this repo's <c>ModManager.Tests</c> project has no
    /// <c>InternalsVisibleTo</c> grant from Infrastructure, and a pure, stateless parser has no real
    /// encapsulation to protect by staying internal.
    /// </summary>
    public static IReadOnlyList<SiteObservation> ParseModCards(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        List<SiteObservation> observations = [];
        MatchCollection cardStarts = ModCardStart.Matches(html);

        for (int i = 0; i < cardStarts.Count; i++)
        {
            Match cardStart = cardStarts[i];
            string modKey = cardStart.Groups[1].Value;

            int blockStart = cardStart.Index;
            int blockEnd = i + 1 < cardStarts.Count ? cardStarts[i + 1].Index : html.Length;
            string block = html[blockStart..blockEnd];

            string? version = ExtractGroup(VersionBadge, block);
            string? updatedOnRaw = ExtractGroup(LastUpdate, block);
            string? title = ExtractGroup(SearchTitle, block);
            string? downloadUrl = ExtractGroup(PrimaryDownloadLink, block);

            observations.Add(new SiteObservation(new SiteModKey(modKey), version, updatedOnRaw, title, downloadUrl, null));
        }

        return observations;
    }

    private static string? ExtractGroup(Regex pattern, string input)
    {
        Match match = pattern.Match(input);
        if (!match.Success)
        {
            return null;
        }

        string value = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
        return value.Length == 0 ? null : value;
    }

    private static string? GetFragment(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        int fragmentIndex = url.IndexOf('#');
        if (fragmentIndex < 0 || fragmentIndex == url.Length - 1)
        {
            return null;
        }

        return url[(fragmentIndex + 1)..];
    }
}
