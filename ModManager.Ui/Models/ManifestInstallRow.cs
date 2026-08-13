namespace ModManager.Ui.Models;

/// <summary>A manifest install record flattened for display.</summary>
public sealed record ManifestInstallRow(
    string Provider,
    string? Version,
    DateTime InstalledUtc,
    int FileCount,
    string? ModPageUrl,
    string? SourceArchivePath)
{
    public string Summary => Version is null
        ? $"{FileCount} file(s) · installed {InstalledUtc:g}"
        : $"{FileCount} file(s) · installed {InstalledUtc:g} · v{Version}";
}
