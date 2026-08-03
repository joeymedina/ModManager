namespace ModManager.Application.Models;

public sealed record RepositoryState(
    ModsFolderLayout Layout,
    ManifestModel Manifest,
    ManifestProfile Profile,
    IReadOnlyList<ManagedMod> Mods);
