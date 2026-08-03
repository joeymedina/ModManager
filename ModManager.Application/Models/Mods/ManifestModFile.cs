namespace ModManager.Application.Models;

public sealed class ManifestModFile
{
    public required string RelativePath { get; set; }

    public ModFileState State { get; set; }
}
