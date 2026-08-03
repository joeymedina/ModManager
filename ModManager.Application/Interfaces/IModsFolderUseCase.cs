using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

public interface IModsFolderUseCase
{
    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    ModsFolderLayout GetLayout(string modsFolderPath);

    /// <summary>
    /// Loads discovered mods from both active and disabled folders.
    /// </summary>
    Task<IReadOnlyList<ManagedMod>> LoadModsAsync(string modsFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves all files for a mod into the active mods folder.
    /// </summary>
    Task<ManagedMod> EnableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves all files for a mod into the disabled mods folder.
    /// </summary>
    Task<ManagedMod> DisableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all files associated with a mod from both locations.
    /// </summary>
    Task DeleteModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default);
}
