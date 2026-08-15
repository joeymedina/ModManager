using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// Shared by every write path that can produce an <see cref="UpdateTracking"/> baseline — adoption,
/// a fresh install, and (for lookup rather than construction) supersede detection — so the
/// host-to-strategy matching and mod-key resolution live in exactly one place instead of being
/// re-implemented per caller.
/// </summary>
public sealed class SiteTrackingResolver(IEnumerable<IModSiteStrategy> siteStrategies, TimeSpan? fetchTimeout = null, ILogger<SiteTrackingResolver>? logger = null)
{
    private static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IModSiteStrategy> _siteStrategies = [.. siteStrategies];
    private readonly TimeSpan _fetchTimeout = fetchTimeout ?? DefaultFetchTimeout;
    private readonly ILogger<SiteTrackingResolver> _logger = logger ?? NullLogger<SiteTrackingResolver>.Instance;

    /// <summary>
    /// Finds the registered strategy whose <see cref="IModSiteStrategy.Hosts"/> matches
    /// <paramref name="modPageUrl"/>'s host, and asks it to resolve a mod key from
    /// <paramref name="hints"/>. Null when there's no URL, it doesn't parse, or no strategy is
    /// registered for that host — never when a strategy matched but couldn't resolve a key (that
    /// case is a matched strategy with a null <c>ResolvedKey</c>, still useful to the caller).
    /// </summary>
    public (IModSiteStrategy Strategy, SiteModKey? ResolvedKey)? TryMatchStrategy(string? modPageUrl, ModKeyHints hints)
    {
        if (string.IsNullOrWhiteSpace(modPageUrl) || !Uri.TryCreate(modPageUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        IModSiteStrategy? strategy = _siteStrategies.FirstOrDefault(
            candidate => candidate.Hosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)));

        return strategy is null ? null : (strategy, strategy.TryResolveModKey(hints));
    }

    /// <summary>
    /// Builds a fresh <see cref="UpdateTracking"/> baseline for a URL. Only set when a registered
    /// strategy's host actually matches — see the callers for why an unmatched host stays untracked
    /// rather than getting a made-up site key.
    /// </summary>
    public UpdateTracking? ResolveTracking(string? modPageUrl, string? version, string displayName, IReadOnlyList<string> installedRelativePaths)
    {
        ModKeyHints hints = new(modPageUrl, null, displayName, installedRelativePaths);
        (IModSiteStrategy Strategy, SiteModKey? ResolvedKey)? match = TryMatchStrategy(modPageUrl, hints);

        return match is null
            ? null
            : new UpdateTracking(match.Value.Strategy.SiteKey, match.Value.ResolvedKey?.Value, modPageUrl!, version, null, DateTime.UtcNow);
    }

    /// <summary>
    /// Best-effort live lookup of a mod's current version, for prefilling the install dialog's
    /// Version field the moment its mod page URL resolves — the "prefilled from the site" behavior
    /// the design always wanted, not available at <see cref="ResolveTracking"/> time since that's
    /// synchronous and this needs a network fetch. Never throws: a missing match, an unresolvable
    /// key, a timeout, or any fetch failure all just return null, since this is a convenience on top
    /// of the primary install flow, not a step it can depend on succeeding.
    /// </summary>
    public async Task<string?> TryFetchCurrentVersionAsync(string? modPageUrl, string displayName, CancellationToken cancellationToken = default)
    {
        ModKeyHints hints = new(modPageUrl, null, displayName, []);
        (IModSiteStrategy Strategy, SiteModKey? ResolvedKey)? match = TryMatchStrategy(modPageUrl, hints);
        if (match is not { ResolvedKey: not null })
        {
            return null;
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_fetchTimeout);

        try
        {
            IReadOnlyList<SiteObservation> observations = await match.Value.Strategy.FetchObservationsAsync([match.Value.ResolvedKey], timeoutSource.Token);
            return observations.FirstOrDefault(observation => string.Equals(observation.ModKey.Value, match.Value.ResolvedKey!.Value, StringComparison.OrdinalIgnoreCase))?.Version;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out fetching the current version for {ModPageUrl} from {SiteKey}", modPageUrl, match.Value.Strategy.SiteKey);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch the current version for {ModPageUrl} from {SiteKey}", modPageUrl, match.Value.Strategy.SiteKey);
            return null;
        }
    }
}
