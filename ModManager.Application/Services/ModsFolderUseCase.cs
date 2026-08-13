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
    /// Discovers mod files from both active and disabled folders. Never writes.
    /// </summary>
    public Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.LoadFilesAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Moves the given files into the active mods folder. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.EnableAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Moves the given files into the disabled mods folder. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.DisableAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Deletes the given files from active and/or disabled folders. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.DeleteAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Links already-discovered files to a source by writing an InstallRecord that covers their
    /// current paths. Metadata only — never moves or extracts anything.
    /// </summary>
    public Task<ArchiveInstallResult<InstallRecord>> AdoptAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string displayName,
        string? modPageUrl,
        string? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return repository.AdoptAsync(modsFolderPath, relativePaths, displayName, modPageUrl, version, cancellationToken);
    }

    /// <summary>
    /// Loads the manifest's group definitions. Never writes.
    /// </summary>
    public Task<IReadOnlyList<ModGroup>> LoadGroupsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.LoadGroupsAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Adds the given files to a group, creating or reusing it by name. A file belongs to at most one
    /// group.
    /// </summary>
    public Task<ArchiveInstallResult<ModGroup>> AddToGroupAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        return repository.AddToGroupAsync(modsFolderPath, relativePaths, groupName, cancellationToken);
    }

    /// <summary>
    /// Removes the given paths from whatever group they belong to.
    /// </summary>
    public Task RemoveFromGroupAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.RemoveFromGroupAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Sets (or clears, when <paramref name="category"/> is null or blank) the category on the given
    /// files.
    /// </summary>
    public Task<ArchiveInstallResult<string?>> SetCategoryAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string? category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.SetCategoryAsync(modsFolderPath, relativePaths, category, cancellationToken);
    }

    /// <summary>
    /// Loads the full manifest for display. Never writes.
    /// </summary>
    public Task<ModsManifest> LoadManifestAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.LoadManifestAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Reads the manifest file's raw JSON text, for display verbatim.
    /// </summary>
    public Task<ManifestRawContent> ReadManifestRawAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.ReadManifestRawAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Parses and saves manually edited manifest JSON. Rejects (without writing) JSON that doesn't
    /// parse or uses an unsupported schema version.
    /// </summary>
    public Task<ArchiveInstallResult<ModsManifest>> SaveManifestRawAsync(string modsFolderPath, string rawJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(rawJson);
        return repository.SaveManifestRawAsync(modsFolderPath, rawJson, cancellationToken);
    }
}
