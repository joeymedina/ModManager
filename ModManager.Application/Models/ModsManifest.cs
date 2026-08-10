using System.Text.Json.Serialization;

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

    /// <summary>
    /// True when this instance is an empty stand-in for a manifest that existed on disk but could not
    /// be read. Saving over it would destroy the user's real install history, so writes are refused.
    /// Never serialized — it describes this load, not the file.
    /// </summary>
    [JsonIgnore]
    public string? UnreadableReason { get; init; }
}
