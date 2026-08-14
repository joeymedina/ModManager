namespace ModManager.Application.Models;

/// <summary>
/// What a site currently says about one of its mods — a strategy's only output. Deliberately not a
/// verdict: whether this means an update is available is comparison policy, and that lives in the
/// base service so every strategy doesn't re-implement (and drift on) normalization and precedence.
/// </summary>
public sealed record SiteObservation(
    SiteModKey ModKey,
    string? Version,
    string? UpdatedOnRaw,
    string? Title,
    string? DownloadUrl,
    string? ModPageUrl);
