using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

/// <summary>
/// Reads one site's mod pages so <see cref="IModSiteUpdateService"/> can check installs tracked
/// against it. A strategy answers exactly two questions — which mod on this site is this record, and
/// what does the site currently say about these mods — and nothing else: whether that adds up to an
/// update, version normalization, and the three-state outcome are policy, and stay in the base
/// service so every strategy doesn't re-implement (and drift on) them.
///
/// A strategy takes a constructor-injected <see cref="IModPageFetcher"/> to do its own fetching —
/// that seam is what lets an authenticated-session site be served by a different fetcher later without
/// reshaping this interface. What a strategy must never touch is the filesystem or the manifest; those
/// stay with the base service and its callers, which is what makes an untrusted third-party strategy
/// safe to load. A strategy that throws or hangs degrades that site's own records to
/// <see cref="SiteUpdateStatus.Indeterminate"/> without affecting any other site's results — that
/// containment lives in the base service, not here, precisely so a strategy can't be relied on to
/// behave.
/// </summary>
public interface IModSiteStrategy
{
    /// <summary>
    /// Stable key this strategy is registered under, e.g. "sacrificialmods.com". Matched against
    /// <see cref="UpdateTracking.SiteKey"/>.
    /// </summary>
    string SiteKey { get; }

    /// <summary>
    /// Hosts a tracking URL's authority is checked against to route a fresh install to this strategy.
    /// </summary>
    IReadOnlyList<string> Hosts { get; }

    SiteCapabilities Capabilities { get; }

    /// <summary>
    /// Resolves which of this site's mods <paramref name="hints"/> refers to. Returns
    /// <see langword="null"/> when nothing in the hints is enough to tell — the base service leaves
    /// the record unresolved and retries this on the next check rather than guessing.
    /// </summary>
    SiteModKey? TryResolveModKey(ModKeyHints hints);

    /// <summary>
    /// Reports what the site currently says about each of the given mods, batched — a site whose mods
    /// share one page (Sacrificial) fetches it once regardless of how many keys are passed; a
    /// one-page-per-mod site fetches per key, at whatever concurrency it chooses. A key this strategy
    /// has no observation for (mod removed, parse failed for just that section) is simply absent from
    /// the result rather than represented with a null entry — the base service treats an absent key as
    /// "not found on the site".
    /// </summary>
    Task<IReadOnlyList<SiteObservation>> FetchObservationsAsync(IReadOnlyList<SiteModKey> modKeys, CancellationToken cancellationToken = default);
}
