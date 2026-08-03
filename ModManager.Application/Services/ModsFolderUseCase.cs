using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Application.Services;

public sealed class ModsFolderUseCase(IModsFolderRepository repository) : IModsFolderUseCase
{
    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.GetLayout(modsFolderPath);
    }

    /// <summary>
    /// Loads discovered mods from both active and disabled folders.
    /// </summary>
    public Task<IReadOnlyList<ManagedMod>> LoadModsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.LoadModsAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Moves all files for a mod into the active mods folder.
    /// </summary>
    public Task<ManagedMod> EnableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return repository.EnableModAsync(modsFolderPath, modId, cancellationToken);
    }

    /// <summary>
    /// Moves all files for a mod into the disabled mods folder.
    /// </summary>
    public Task<ManagedMod> DisableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return repository.DisableModAsync(modsFolderPath, modId, cancellationToken);
    }

    /// <summary>
    /// Deletes all files associated with a mod from both locations.
    /// </summary>
    public Task DeleteModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return repository.DeleteModAsync(modsFolderPath, modId, cancellationToken);
    }
}
