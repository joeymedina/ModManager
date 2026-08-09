namespace ModManager.Application.Models;

public sealed record ManifestFileEntry(string RelativePath, string? DisplayName = null, string? GroupId = null, string? Notes = null);

public sealed record ModGroup(string GroupId, string Name, IReadOnlyList<string> Members);

public sealed record ModsManifest(
    int SchemaVersion,
    IReadOnlyList<ManifestFileEntry> Files,
    IReadOnlyList<ModGroup> Groups,
    IReadOnlyList<InstallRecord> Installs)
{
    public const int CurrentSchemaVersion = 1;

    public static ModsManifest Empty { get; } = new(CurrentSchemaVersion, [], [], []);
}
