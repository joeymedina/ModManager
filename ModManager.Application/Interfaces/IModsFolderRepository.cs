using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

public interface IModsFolderRepository
{
    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    ModsFolderLayout GetLayout(string modsFolderPath);

    /// <summary>
    /// Discovers mod files from both active and disabled folders. Never writes.
    /// </summary>
    Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the given files into the active mods folder. Continues past per-file failures.
    /// </summary>
    Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the given files into the disabled mods folder. Continues past per-file failures.
    /// </summary>
    Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the given files from active and/or disabled folders. Continues past per-file failures.
    /// </summary>
    Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);
}
