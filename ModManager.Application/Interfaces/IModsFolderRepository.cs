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

    /// <summary>
    /// Links already-discovered files to a source by writing an InstallRecord that covers their
    /// current paths. Metadata only — never moves or extracts anything. All-or-nothing: fails if any
    /// path can't be found under either root.
    /// </summary>
    Task<ArchiveInstallResult<InstallRecord>> AdoptAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string displayName,
        string? modPageUrl,
        string? version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the manifest's group definitions, including members whose path no longer resolves to a
    /// discovered file — the caller renders those as "missing" rather than dropping them. Never
    /// writes.
    /// </summary>
    Task<IReadOnlyList<ModGroup>> LoadGroupsAsync(string modsFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the given (already-discovered) files to a group, creating it if no group with this name
    /// (case-insensitive) exists yet, or reusing it otherwise. A file belongs to at most one group, so
    /// this removes each path from whatever group it previously belonged to. All-or-nothing: fails if
    /// any path can't be found under either root.
    /// </summary>
    Task<ArchiveInstallResult<ModGroup>> AddToGroupAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the given paths from whatever group they belong to. Paths not currently in a group are
    /// a no-op. A group left with no members is dropped.
    /// </summary>
    Task RemoveFromGroupAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);
}
