namespace ModManager.Application.Models;

public sealed record ManagedMod(
    string ModId,
    string Name,
    string PackageKey,
    IReadOnlyList<ManagedModFile> Files,
    bool IsMixedState)
{
    public bool IsEnabled => !IsMixedState && Files.All(file => file.State == ModFileState.Enabled);
}
