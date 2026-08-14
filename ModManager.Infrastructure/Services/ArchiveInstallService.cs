using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// Installs mod archives (and bare .package/.ts4script files) into a per-mod subfolder under the
/// mods folder, and records the result in the per-folder manifest. Replaces the WickedWhims-specific
/// WickedWhimsArchiveInstaller with a general, user-facing pipeline.
/// </summary>
public sealed class ArchiveInstallService(
    ModsManifestService manifestService,
    ModsFileOperationsService fileOperationsService,
    ILogger<ArchiveInstallService>? logger = null) : IArchiveInstallService
{
    private static readonly HashSet<string> ModExtensions = new(StringComparer.OrdinalIgnoreCase) { ".package", ".ts4script" };
    private static readonly Regex VariantFolderPattern = new(@"(^|[\\/])(optional|alternate|extras?)([\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<ArchiveInstallService> _logger = logger ?? NullLogger<ArchiveInstallService>.Instance;

    public Task<ArchiveInstallResult<ArchivePreview>> PreviewAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!File.Exists(archivePath))
        {
            _logger.LogWarning("Preview failed: {ArchivePath} does not exist", archivePath);
            return Task.FromResult(ArchiveInstallResult<ArchivePreview>.Fail($"File not found: {archivePath}"));
        }

        string extension = Path.GetExtension(archivePath);
        if (ModExtensions.Contains(extension))
        {
            ArchiveEntryPreview single = new(Path.GetFileName(archivePath), ArchiveEntryKind.Installable, SelectedByDefault: true);
            return Task.FromResult(ArchiveInstallResult<ArchivePreview>.Ok(new ArchivePreview([single])));
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Preview failed: '{Extension}' is not a supported archive type ({ArchivePath})", extension, archivePath);
            return Task.FromResult(ArchiveInstallResult<ArchivePreview>.Fail(NonZipMessage(extension)));
        }

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            IReadOnlyList<ArchiveEntryPreview> entries = ClassifyEntries(archive.Entries);
            _logger.LogDebug(
                "Previewed {ArchivePath}: {InstallableCount} installable, {VariantCount} variant, {OtherCount} not installable",
                archivePath,
                entries.Count(entry => entry.Kind == ArchiveEntryKind.Installable),
                entries.Count(entry => entry.Kind == ArchiveEntryKind.Variant),
                entries.Count(entry => entry.Kind == ArchiveEntryKind.NotInstallable));

            return Task.FromResult(ArchiveInstallResult<ArchivePreview>.Ok(new ArchivePreview(entries)));
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Preview failed: {ArchivePath} is not a valid zip archive", archivePath);
            return Task.FromResult(ArchiveInstallResult<ArchivePreview>.Fail("Not a valid zip archive."));
        }
    }

    public async Task<ArchiveInstallResult<InstallRecord>> InstallAsync(
        string archivePath,
        IReadOnlySet<string> selectedEntryNames,
        ModsFolderLayout layout,
        string displayName,
        string? category,
        InstallSource source,
        string? version,
        InstallRecord? supersedes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(selectedEntryNames);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(source);

        if (!File.Exists(archivePath))
        {
            _logger.LogWarning("Install failed: {ArchivePath} does not exist", archivePath);
            return ArchiveInstallResult<InstallRecord>.Fail($"File not found: {archivePath}");
        }

        Directory.CreateDirectory(layout.ModsFolderPath);
        string installRoot = supersedes is null ? layout.ModsFolderPath : ModsFolderPathService.ResolveInstallRoot(layout, supersedes);
        Directory.CreateDirectory(installRoot);

        // Superseding writes back into the previous record's own folder — the folder name it derives
        // from is whatever displayName produced at first install, not this call's displayName, so an
        // edited name here doesn't fork the mod into a second folder mid-update.
        string? supersededModFolder = ResolveSupersededModFolder(installRoot, supersedes);
        string modFolderName = supersededModFolder is not null ? Path.GetFileName(supersededModFolder) : ModsFolderPathService.ResolveDedupedFolderName(displayName, installRoot);
        string targetRoot = supersededModFolder ?? Path.Combine(installRoot, modFolderName);

        if (supersededModFolder is null && !string.Equals(modFolderName, displayName.Trim(), StringComparison.Ordinal))
        {
            _logger.LogInformation("Installing \"{DisplayName}\" into folder {ModFolderName} (name was sanitized or already taken)", displayName, modFolderName);
        }

        _logger.LogInformation("Installing {ArchivePath} into {TargetRoot} from {Provider}", archivePath, targetRoot, source.Provider);

        string extension = Path.GetExtension(archivePath);
        ArchiveInstallResult<InstallRecord>? bareFileResult = TryInstallBareFile(archivePath, extension, targetRoot, installRoot, source, version, cancellationToken);
        if (bareFileResult is not null)
        {
            if (!bareFileResult.Success)
            {
                return bareFileResult;
            }

            await FinishInstallAsync(layout, installRoot, displayName, category, bareFileResult.Value!, supersedes, cancellationToken);
            return bareFileResult;
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Install failed: '{Extension}' is not a supported archive type ({ArchivePath})", extension, archivePath);
            return ArchiveInstallResult<InstallRecord>.Fail(NonZipMessage(extension));
        }

        InstallRecord record;
        try
        {
            Directory.CreateDirectory(targetRoot);
            record = ExtractZip(archivePath, selectedEntryNames, targetRoot, installRoot, source, version);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Install failed: {ArchivePath} is not a valid zip archive", archivePath);
            return ArchiveInstallResult<InstallRecord>.Fail("Not a valid zip archive.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Install of {ArchivePath} rejected", archivePath);
            return ArchiveInstallResult<InstallRecord>.Fail(ex.Message);
        }

        await FinishInstallAsync(layout, installRoot, displayName, category, record, supersedes, cancellationToken);
        _logger.LogInformation(
            "Installed \"{DisplayName}\" as install {InstallId}: {InstalledCount} file(s) into {TargetRoot}, {SkippedCount} skipped",
            displayName,
            record.InstallId,
            record.Files.Count,
            targetRoot,
            record.SkippedEntries.Count);

        return ArchiveInstallResult<InstallRecord>.Ok(record);
    }

    private static ArchiveInstallResult<InstallRecord>? TryInstallBareFile(
        string archivePath,
        string extension,
        string targetRoot,
        string installRoot,
        InstallSource source,
        string? version,
        CancellationToken cancellationToken)
    {
        if (!ModExtensions.Contains(extension))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(targetRoot);
        string fileName = Path.GetFileName(archivePath);
        string targetPath = Path.Combine(targetRoot, fileName);
        File.Copy(archivePath, targetPath, overwrite: true);

        // Relative to installRoot, matching ExtractZip below — not just the bare filename, which
        // would leave every store keyed on RelativePath (the manifest entry, group membership, this
        // record itself) unable to match what ModsDiscoveryService reports for the same file.
        string relativePath = Path.GetRelativePath(installRoot, targetPath).Replace(Path.DirectorySeparatorChar, '/');
        InstallRecordFile installed = new(relativePath, FileHashing.ComputeSha256(targetPath), new FileInfo(targetPath).Length);
        InstallRecord record = new(
            Guid.NewGuid().ToString("N"),
            source,
            version,
            DateTime.UtcNow,
            archivePath,
            [installed],
            []);

        return ArchiveInstallResult<InstallRecord>.Ok(record);
    }

    private InstallRecord ExtractZip(
        string archivePath,
        IReadOnlySet<string> selectedEntryNames,
        string targetRoot,
        string modsFolderPath,
        InstallSource source,
        string? version)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        string rootWithSeparator = Path.GetFullPath(targetRoot) + Path.DirectorySeparatorChar;

        List<InstallRecordFile> installed = [];
        List<string> skipped = [];

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (!selectedEntryNames.Contains(entry.FullName))
            {
                _logger.LogDebug("Skipped archive entry {EntryName} (not selected)", entry.FullName);
                skipped.Add(entry.FullName);
                continue;
            }

            string entryRelativePath = entry.FullName.Replace('\\', '/');
            if (Path.GetExtension(entry.Name).Equals(".ts4script", StringComparison.OrdinalIgnoreCase)
                && entryRelativePath.Contains('/'))
            {
                // A .ts4script won't load more than one folder below the Mods root; the mod folder
                // itself is that one level, so flatten any nested archive path to the mod folder root.
                entryRelativePath = entry.Name;
                _logger.LogInformation("Flattened {EntryName} to the mod folder root — a .ts4script won't load from a nested folder", entry.FullName);
            }

            string targetPath = Path.GetFullPath(Path.Combine(targetRoot, entryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                _logger.LogWarning("Rejected archive entry escaping {TargetRoot}: {EntryName} (from {ArchivePath})", targetRoot, entry.FullName, archivePath);
                throw new InvalidOperationException($"Unsafe archive path: {entry.FullName}");
            }

            string? directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"Could not resolve directory for archive entry '{entry.FullName}'.");
            }

            Directory.CreateDirectory(directory);
            entry.ExtractToFile(targetPath, overwrite: true);

            string relativeToModsFolder = Path.GetRelativePath(modsFolderPath, targetPath).Replace(Path.DirectorySeparatorChar, '/');
            InstallRecordFile installedFile = new(relativeToModsFolder, FileHashing.ComputeSha256(targetPath), new FileInfo(targetPath).Length);
            installed.Add(installedFile);

            _logger.LogDebug(
                "Extracted {EntryName} to {RelativePath} ({SizeBytes} bytes, sha256 {Sha256})",
                entry.FullName,
                installedFile.RelativePath,
                installedFile.SizeBytes,
                installedFile.Sha256);
        }

        return new InstallRecord(
            Guid.NewGuid().ToString("N"),
            source,
            version,
            DateTime.UtcNow,
            archivePath,
            installed,
            skipped);
    }

    /// <summary>
    /// When superseding, this install's target folder is the previous record's own folder rather than
    /// a freshly minted one — the first path segment every one of a record's files shares, since a
    /// zip extracts flat into one target folder and a bare file lands directly in one too. Returns
    /// <see langword="null"/> for a fresh install or a record with no files to derive a folder from.
    /// </summary>
    private static string? ResolveSupersededModFolder(string installRoot, InstallRecord? supersedes)
    {
        if (supersedes is null || supersedes.Files.Count == 0)
        {
            return null;
        }

        string firstSegment = supersedes.Files[0].RelativePath.Split('/', 2)[0];
        return string.IsNullOrWhiteSpace(firstSegment) ? null : Path.Combine(installRoot, firstSegment);
    }

    /// <summary>
    /// Prunes files the superseded record wrote that this install doesn't, then persists. Pruning
    /// happens before the manifest write so a failure here leaves the previous record intact rather
    /// than pointing at files that were already deleted.
    /// </summary>
    private async Task FinishInstallAsync(
        ModsFolderLayout layout,
        string installRoot,
        string displayName,
        string? category,
        InstallRecord record,
        InstallRecord? supersedes,
        CancellationToken cancellationToken)
    {
        if (supersedes is not null)
        {
            HashSet<string> newPaths = new(record.Files.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
            List<string> stalePaths = [.. supersedes.Files.Select(file => file.RelativePath).Where(path => !newPaths.Contains(path))];

            IReadOnlyList<ModFileFailure> failures = await fileOperationsService.DeleteStalePathsAsync(installRoot, stalePaths, cancellationToken);
            foreach (ModFileFailure failure in failures)
            {
                _logger.LogWarning(
                    "Could not delete stale file {RelativePath} while superseding install {InstallId}: {Reason}",
                    failure.RelativePath,
                    supersedes.InstallId,
                    failure.Reason);
            }
        }

        await PersistRecordAsync(layout, displayName, category, record, supersedes, cancellationToken);
    }

    /// <summary>
    /// Writes the record and its file metadata into the manifest. When superseding, the previous
    /// record is replaced rather than kept alongside the new one, and any <see cref="ManifestFileEntry"/>
    /// left over from a path the new install doesn't include is dropped rather than orphaned. A path
    /// that's identical between the old and new record (the common case — an archive's internal
    /// layout is usually stable release to release) keeps its existing <c>GroupId</c>/<c>Notes</c>
    /// instead of losing them, which also fixes the same loss for a plain reinstall over unchanged
    /// paths outside of any supersede.
    /// </summary>
    private async Task PersistRecordAsync(ModsFolderLayout layout, string displayName, string? category, InstallRecord record, InstallRecord? supersedes, CancellationToken cancellationToken)
    {
        ModsManifest manifest = await manifestService.LoadAsync(layout, cancellationToken);

        HashSet<string> installedPaths = [.. record.Files.Select(file => file.RelativePath)];
        HashSet<string> stalePaths = supersedes is null
            ? []
            : new HashSet<string>(supersedes.Files.Select(file => file.RelativePath).Where(path => !installedPaths.Contains(path)), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ManifestFileEntry> existingByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestFileEntry entry in manifest.Files)
        {
            existingByPath[entry.RelativePath] = entry;
        }

        List<ManifestFileEntry> files = [.. manifest.Files.Where(entry => !installedPaths.Contains(entry.RelativePath) && !stalePaths.Contains(entry.RelativePath))];
        files.AddRange(record.Files.Select(file =>
        {
            existingByPath.TryGetValue(file.RelativePath, out ManifestFileEntry? existing);
            return new ManifestFileEntry(file.RelativePath, displayName, existing?.GroupId, existing?.Notes, category);
        }));

        List<InstallRecord> installs = supersedes is null
            ? [.. manifest.Installs, record]
            : [.. manifest.Installs.Where(existingRecord => existingRecord.InstallId != supersedes.InstallId), record];

        ModsManifest updated = manifest with { Files = files, Installs = installs };
        await manifestService.SaveAsync(layout, updated, cancellationToken);
    }

    private static IReadOnlyList<ArchiveEntryPreview> ClassifyEntries(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        List<ArchiveEntryPreview> previews = [];
        List<ZipArchiveEntry> packageEntries = [];

        foreach (ZipArchiveEntry entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string extension = Path.GetExtension(entry.Name);
            if (extension.Equals(".package", StringComparison.OrdinalIgnoreCase))
            {
                packageEntries.Add(entry);
                continue;
            }

            if (extension.Equals(".ts4script", StringComparison.OrdinalIgnoreCase))
            {
                bool inVariantFolder = VariantFolderPattern.IsMatch(entry.FullName);
                previews.Add(new ArchiveEntryPreview(
                    entry.FullName,
                    inVariantFolder ? ArchiveEntryKind.Variant : ArchiveEntryKind.Installable,
                    SelectedByDefault: !inVariantFolder));
                continue;
            }

            previews.Add(new ArchiveEntryPreview(entry.FullName, ArchiveEntryKind.NotInstallable, SelectedByDefault: false));
        }

        // ponytail: "sibling .package files with the same stem" is inherently a guess (the doc says
        // so itself) — reuses the same up-to-first-delimiter heuristic the old DerivePackageKey used
        // for mod identity, just repurposed here to flag *candidate* variants for the user to review.
        foreach (IGrouping<(string Dir, string Stem), ZipArchiveEntry> group in packageEntries.GroupBy(
            entry => (Path.GetDirectoryName(entry.FullName) ?? string.Empty, StemOf(entry.Name))))
        {
            bool sharesStemWithSibling = group.Count() > 1;
            foreach (ZipArchiveEntry entry in group)
            {
                bool isVariant = sharesStemWithSibling || VariantFolderPattern.IsMatch(entry.FullName);
                previews.Add(new ArchiveEntryPreview(
                    entry.FullName,
                    isVariant ? ArchiveEntryKind.Variant : ArchiveEntryKind.Installable,
                    SelectedByDefault: !isVariant));
            }
        }

        return previews;
    }

    private static string StemOf(string fileName)
    {
        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        int delimiterIndex = nameOnly.IndexOfAny(['_', '-']);
        return delimiterIndex < 0 ? nameOnly : nameOnly[..delimiterIndex];
    }

    private static string NonZipMessage(string extension) =>
        string.IsNullOrEmpty(extension)
            ? "Unsupported file type. Extract manually, then use Install from file."
            : $"'{extension}' archives aren't supported yet — extract manually, then use Install from file.";
    // ponytail: System.IO.Compression is zip-only. SharpCompress would add .rar/.7z support if this
    // turns out to matter in practice.
}
