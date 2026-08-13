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
public sealed class ArchiveInstallService(ModsManifestService manifestService, ILogger<ArchiveInstallService>? logger = null) : IArchiveInstallService
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
        string modFolderName = ResolveModFolderName(layout.ModsFolderPath, displayName);
        string targetRoot = Path.Combine(layout.ModsFolderPath, modFolderName);

        if (!string.Equals(modFolderName, displayName.Trim(), StringComparison.Ordinal))
        {
            _logger.LogInformation("Installing \"{DisplayName}\" into folder {ModFolderName} (name was sanitized or already taken)", displayName, modFolderName);
        }

        _logger.LogInformation("Installing {ArchivePath} into {TargetRoot} from {Provider}", archivePath, targetRoot, source.Provider);

        string extension = Path.GetExtension(archivePath);
        ArchiveInstallResult<InstallRecord>? bareFileResult = TryInstallBareFile(archivePath, extension, targetRoot, source, version, cancellationToken);
        if (bareFileResult is not null)
        {
            if (!bareFileResult.Success)
            {
                return bareFileResult;
            }

            await PersistRecordAsync(layout, displayName, category, bareFileResult.Value!, cancellationToken);
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
            record = ExtractZip(archivePath, selectedEntryNames, targetRoot, layout.ModsFolderPath, source, version);
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

        await PersistRecordAsync(layout, displayName, category, record, cancellationToken);
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

        InstallRecordFile installed = new(fileName, FileHashing.ComputeSha256(targetPath), new FileInfo(targetPath).Length);
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

    private async Task PersistRecordAsync(ModsFolderLayout layout, string displayName, string? category, InstallRecord record, CancellationToken cancellationToken)
    {
        ModsManifest manifest = await manifestService.LoadAsync(layout, cancellationToken);

        HashSet<string> installedPaths = [.. record.Files.Select(file => file.RelativePath)];
        List<ManifestFileEntry> files = [.. manifest.Files.Where(entry => !installedPaths.Contains(entry.RelativePath))];
        files.AddRange(record.Files.Select(file => new ManifestFileEntry(file.RelativePath, displayName, Category: category)));

        ModsManifest updated = manifest with { Files = files, Installs = [.. manifest.Installs, record] };
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

    private static string ResolveModFolderName(string modsFolderPath, string displayName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new([.. displayName.Trim().Where(c => !invalidChars.Contains(c))]);
        if (sanitized.Length == 0)
        {
            sanitized = "Mod";
        }

        string candidate = sanitized;
        for (int suffix = 2; Directory.Exists(Path.Combine(modsFolderPath, candidate)); suffix++)
        {
            candidate = $"{sanitized} ({suffix})";
        }

        return candidate;
    }

    private static string NonZipMessage(string extension) =>
        string.IsNullOrEmpty(extension)
            ? "Unsupported file type. Extract manually, then use Install from file."
            : $"'{extension}' archives aren't supported yet — extract manually, then use Install from file.";
    // ponytail: System.IO.Compression is zip-only. SharpCompress would add .rar/.7z support if this
    // turns out to matter in practice.
}
