namespace ModManager.Application.Models;

/// <summary>
/// The baseline a site-based update check compares against, hung off an <see cref="InstallRecord"/>.
/// Deliberately separate from <see cref="InstallRecord.Version"/> and <see cref="InstallSource.ModPageUrl"/>:
/// those are install history and provenance, while this is what a check compares against and where it
/// looks — re-pointing a bad URL or dismissing a false positive rewrites this without falsifying either.
/// <see cref="SiteModKey"/> is null when the site is known but the specific mod hasn't resolved yet
/// (e.g. a captured page URL with no usable fragment); it is retried on every check rather than
/// blocking on it.
/// </summary>
public sealed record UpdateTracking(
    string SiteKey,
    string? SiteModKey,
    string TrackingUrl,
    string? BaselineVersion,
    string? BaselineUpdatedOnRaw,
    DateTime BaselineCapturedUtc);
