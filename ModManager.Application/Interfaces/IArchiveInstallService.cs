using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

public interface IArchiveInstallService
{
    /// <summary>
    /// Classifies the entries of an archive (or a bare .package/.ts4script file) without writing
    /// anything, so the caller can present a selection UI before installing.
    /// </summary>
    Task<ArchiveInstallResult<ArchivePreview>> PreviewAsync(string archivePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the selected entries into a new, deduped folder under the mods folder, hashes what
    /// was written, records the install in the per-folder manifest, and returns the resulting
    /// <see cref="InstallRecord"/>.
    /// </summary>
    Task<ArchiveInstallResult<InstallRecord>> InstallAsync(
        string archivePath,
        IReadOnlySet<string> selectedEntryNames,
        ModsFolderLayout layout,
        string displayName,
        string? category,
        InstallSource source,
        string? version,
        CancellationToken cancellationToken = default);
}
