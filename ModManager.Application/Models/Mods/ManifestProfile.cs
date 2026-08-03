namespace ModManager.Application.Models;

public sealed class ManifestProfile
{
    public required string ModsFolderPath { get; set; }

    public required List<ManifestMod> Mods { get; set; }
}
