namespace ModManager.Ui.Models;

/// <summary>A manifest group for display.</summary>
public sealed record ManifestGroupRow(string Name, IReadOnlyList<string> Members);
