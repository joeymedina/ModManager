namespace ModManager.Application.Models;

/// <summary>
/// The three states a site check can land on. Indeterminate is not an edge case to special-case away —
/// a broken scraper and "up to date" must never be indistinguishable, so every path that can't produce
/// a real comparison lands here instead of guessing.
/// </summary>
public enum SiteUpdateStatus
{
    UpToDate = 0,
    UpdateAvailable = 1,
    Indeterminate = 2
}

/// <summary>
/// The outcome of checking one tracked install against its site, plus enough of what was observed to
/// show in the UI and to feed "mark as current" without a second fetch. <see cref="ResolvedModKey"/>
/// is set when the record's <see cref="UpdateTracking.SiteModKey"/> was null going in but resolved
/// successfully this check — the caller decides whether/how to persist it back into the manifest,
/// since this service stays manifest-write-free like the strategies it calls.
/// </summary>
public sealed record SiteUpdateCheckResult(
    string InstallId,
    SiteUpdateStatus Status,
    string? ObservedVersion,
    string? ObservedUpdatedOnRaw,
    string? Reason,
    DateTime CheckedUtc,
    SiteModKey? ResolvedModKey = null)
{
    public bool IsUpdateAvailable => Status == SiteUpdateStatus.UpdateAvailable;
}
