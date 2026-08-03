using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// File-system repository for loading and managing Sims 4 mods in Mods and Mods.Disabled.
/// </summary>
public sealed class ModsFolderService : IModsFolderRepository
{
    private readonly ModsFolderPathService _pathService;
    private readonly ModsManifestService _manifestService;
    private readonly ModsDiscoveryService _discoveryService;
    private readonly ModsFileOperationsService _fileOperationsService;

    public ModsFolderService(): 
        this(
            new ModsFolderPathService(),
            new ModsManifestService(),
            new ModsDiscoveryService(),
            new ModsFileOperationsService(new ModsFolderPathService()))
    {
    }

    public ModsFolderService(
        ModsFolderPathService pathService,
        ModsManifestService manifestService,
        ModsDiscoveryService discoveryService,
        ModsFileOperationsService fileOperationsService)
    {
        _pathService = pathService;
        _manifestService = manifestService;
        _discoveryService = discoveryService;
        _fileOperationsService = fileOperationsService;
    }

    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        return _pathService.GetLayout(modsFolderPath);
    }

    /// <summary>
    /// Loads discovered mods from both active and disabled folders.
    /// </summary>
    public async Task<IReadOnlyList<ManagedMod>> LoadModsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        RepositoryState state = await LoadStateAsync(modsFolderPath, cancellationToken);
        state.Profile.Mods = state.Mods.Select(_discoveryService.ToManifestMod).ToList();
        await _manifestService.SaveManifestAsync(state.Manifest, cancellationToken);

        return state.Mods;
    }

    /// <summary>
    /// Moves all files for a mod into the active mods folder.
    /// </summary>
    public Task<ManagedMod> EnableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        return SetModStateAsync(modsFolderPath, modId, ModFileState.Enabled, cancellationToken);
    }

    /// <summary>
    /// Moves all files for a mod into the disabled mods folder.
    /// </summary>
    public Task<ManagedMod> DisableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        return SetModStateAsync(modsFolderPath, modId, ModFileState.Disabled, cancellationToken);
    }

    /// <summary>
    /// Deletes all files associated with a mod from both locations.
    /// </summary>
    public async Task DeleteModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        RepositoryState state = await LoadStateAsync(modsFolderPath, cancellationToken);
        ManagedMod mod = FindMod(state.Mods, modId);

        await _fileOperationsService.DeleteModFilesAsync(mod, state.Layout, cancellationToken);

        state.Profile.Mods.RemoveAll(existing => string.Equals(existing.ModId, modId, StringComparison.OrdinalIgnoreCase));
        await _manifestService.SaveManifestAsync(state.Manifest, cancellationToken);
    }

    private async Task<ManagedMod> SetModStateAsync(
        string modsFolderPath,
        string modId,
        ModFileState targetState,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        RepositoryState state = await LoadStateAsync(modsFolderPath, cancellationToken);
        ManagedMod mod = FindMod(state.Mods, modId);

        await _fileOperationsService.MoveModFilesForStateChangeAsync(mod, targetState, state.Layout, cancellationToken);

        IReadOnlyList<ManagedMod> updated = await LoadModsAsync(modsFolderPath, cancellationToken);
        ManagedMod? refreshed = updated.FirstOrDefault(existing => string.Equals(existing.ModId, modId, StringComparison.OrdinalIgnoreCase));

        return refreshed ?? throw new InvalidOperationException($"Mod '{modId}' was not found after the move operation completed.");
    }

    private async Task<RepositoryState> LoadStateAsync(string modsFolderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        Directory.CreateDirectory(layout.ModsFolderPath);
        Directory.CreateDirectory(layout.DisabledModsFolderPath);

        ManifestModel manifest = await _manifestService.LoadManifestAsync(cancellationToken);
        ManifestProfile profile = _manifestService.GetOrCreateProfile(manifest, layout.ModsFolderPath);

        IReadOnlyList<ManagedMod> mods = _discoveryService.DiscoverMods(layout, profile);
        return new RepositoryState(layout, manifest, profile, mods);
    }

    private static ManagedMod FindMod(IReadOnlyList<ManagedMod> mods, string modId)
    {
        ManagedMod? mod = mods.FirstOrDefault(existing => string.Equals(existing.ModId, modId, StringComparison.OrdinalIgnoreCase));
        return mod ?? throw new InvalidOperationException($"No mod with ID '{modId}' was found.");
    }
}
