namespace ModManager.Application.Models;

/// <summary>
/// Volatile, re-derivable per-record check history — last checked, last seen, last error. Kept out of
/// the Mods-folder manifest deliberately: a background sweep would otherwise rewrite a file inside the
/// user's Mods folder just to record a timestamp nobody needs to keep. Lives under
/// <c>%LOCALAPPDATA%</c> instead, keyed by <see cref="InstallRecord.InstallId"/>.
/// </summary>
public sealed record UpdateCheckState(
    string InstallId,
    SiteUpdateStatus LastStatus,
    string? LastObservedVersion,
    string? LastObservedUpdatedOnRaw,
    string? LastError,
    DateTime LastCheckedUtc);
