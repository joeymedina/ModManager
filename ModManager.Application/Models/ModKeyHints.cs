namespace ModManager.Application.Models;

/// <summary>
/// Everything a strategy may use to resolve which of its site's mods a record refers to. A strategy
/// uses whichever of these it can — Sacrificial reads the fragment off <see cref="ModPageUrl"/>, a
/// future one-page-per-mod site might match on the URL alone.
/// </summary>
public sealed record ModKeyHints(
    string? ModPageUrl,
    string? DownloadUrl,
    string DisplayName,
    IReadOnlyList<string> InstalledRelativePaths);
