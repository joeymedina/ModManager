namespace ModManager.Application.Models;

/// <summary>
/// The manifest file's raw JSON text, for display verbatim rather than round-tripped through the
/// model. <see cref="Json"/> is null when no manifest file exists yet for the folder.
/// </summary>
public sealed record ManifestRawContent(string Path, bool Exists, string? Json);
