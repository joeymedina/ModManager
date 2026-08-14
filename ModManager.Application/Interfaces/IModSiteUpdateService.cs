using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

/// <summary>
/// Checks tracked installs against their sites: routes each by <see cref="UpdateTracking.SiteKey"/> to
/// a registered <see cref="IModSiteStrategy"/>, resolves any still-unresolved <see cref="SiteModKey"/>,
/// fetches per site (batched), compares against each record's baseline, and persists the result via
/// <see cref="IUpdateCheckStateStore"/>. The single entry point the Updates page calls.
/// </summary>
public interface IModSiteUpdateService
{
    /// <summary>
    /// Checks every given mod. A record with no <see cref="InstallRecord.Tracking"/> is skipped by the
    /// caller before this is called — every mod passed in is expected to be trackable.
    /// </summary>
    Task<IReadOnlyList<SiteUpdateCheckResult>> CheckAsync(IReadOnlyList<TrackedMod> trackedMods, CancellationToken cancellationToken = default);
}
