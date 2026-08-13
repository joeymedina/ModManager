namespace ModManager.Ui.Models;

/// <summary>A manifest file entry flattened for display, with its group id resolved to a name.</summary>
public sealed record ManifestFileRow(string RelativePath, string? DisplayName, string? GroupName, string? Category, string? Notes);
