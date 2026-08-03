namespace ModManager.Application.Models;

public sealed class ManifestMod
{
    public required string ModId { get; set; }

    public required string Name { get; set; }

    public required string PackageKey { get; set; }

    public required List<ManifestModFile> Files { get; set; }
}
