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
    /// Extracts the selected entries, hashes what was written, records the install in the per-folder
    /// manifest, and returns the resulting <see cref="InstallRecord"/>.
    /// </summary>
    /// <param name="supersedes">
    /// When given, this install replaces that record instead of appending a new one: extraction
    /// targets that record's existing install folder (wherever it currently lives, enabled or
    /// disabled) rather than minting a new deduped folder, any file the previous record wrote that
    /// this one doesn't is deleted, and the previous record is removed from the manifest as this one
    /// is added. Pass <see langword="null"/> for a first-time install into a fresh, deduped folder.
    /// </param>
    Task<ArchiveInstallResult<InstallRecord>> InstallAsync(
        string archivePath,
        IReadOnlySet<string> selectedEntryNames,
        ModsFolderLayout layout,
        string displayName,
        string? category,
        InstallSource source,
        string? version,
        InstallRecord? supersedes = null,
        CancellationToken cancellationToken = default);
}
