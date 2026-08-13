using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsUpdateStrategy(
    ModsFolderPathService pathService,
    ModsManifestService manifestService,
    WickedWhimsVersionDetector versionDetector,
    WickedWhimsReleaseClient releaseClient,
    ILogger<WickedWhimsUpdateStrategy>? logger = null) : IModUpdateStrategy
{
    public const string StrategyModId = "wickedwhims";

    private readonly ILogger<WickedWhimsUpdateStrategy> _logger = logger ?? NullLogger<WickedWhimsUpdateStrategy>.Instance;

    public string ModId => StrategyModId;

    /// <summary>
    /// Executes WickedWhims version check and optional download/install. Tracks its own install via
    /// an <see cref="InstallRecord"/> in the manifest (keyed by <see cref="InstallSource.Provider"/> =
    /// <see cref="StrategyModId"/>) so a later update can find the exact root the mod lives in
    /// (enabled or disabled — it no longer assumes enabled) and delete files the new version dropped
    /// instead of leaving both versions installed side by side.
    /// </summary>
    public async Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ModsFolderLayout layout = pathService.GetLayout(request.ModsFolder);
        ModsManifest manifest = await manifestService.LoadAsync(layout, cancellationToken);
        InstallRecord? previousRecord = FindPreviousRecord(manifest);
        string installRoot = ResolveInstallRoot(layout, previousRecord);

        InstalledModVersion installedVersion = versionDetector.FindInstalledVersion(
            installRoot,
            previousRecord?.Files.Select(file => file.RelativePath).ToArray())
            ?? throw new InvalidOperationException($"No WickedWhims version found in {installRoot}.");

        ModReleaseInfo latestRelease = await releaseClient.GetLatestReleaseAsync(cancellationToken);

        int comparison = WickedWhimsVersionDetector.CompareVersions(installedVersion.Version, latestRelease.Version)
            ?? throw new InvalidOperationException("Could not compare installed and latest versions.");

        bool downloadPerformed = false;
        int installedFileCount = 0;

        _logger.LogInformation(
            "WickedWhims check in {InstallRoot}: installed v{InstalledVersion} (from {VersionSource}), latest v{LatestVersion}, comparison {Comparison}",
            installRoot,
            installedVersion.Version,
            installedVersion.Source,
            latestRelease.Version,
            comparison);

        if (comparison < 0 && request.DownloadIfUpdateAvailable)
        {
            WickedWhimsDownload download = await releaseClient.DownloadLatestArchiveAsync(cancellationToken);
            IReadOnlyList<InstallRecordFile> newFiles = ExtractArchive(installRoot, download.Bytes);
            _logger.LogInformation("Extracted {FileCount} WickedWhims v{Version} file(s) into {InstallRoot}", newFiles.Count, latestRelease.Version, installRoot);

            DeleteStaleFiles(installRoot, previousRecord, newFiles);

            InstallRecord newRecord = new(
                Guid.NewGuid().ToString("N"),
                new InstallSource(StrategyModId, WickedWhimsReleaseClient.ItchPage, download.Url),
                latestRelease.Version,
                DateTime.UtcNow,
                null,
                newFiles,
                []);

            await SaveRecordAsync(manifestService, layout, manifest, previousRecord, newRecord, cancellationToken);

            downloadPerformed = true;
            installedFileCount = newFiles.Count;
        }

        return new ModUpdateResult(
            request.ModId,
            new ModVersionInfo(installedVersion.Version, installedVersion.Source),
            latestRelease,
            comparison,
            downloadPerformed,
            installedFileCount);
    }

    private static InstallRecord? FindPreviousRecord(ModsManifest manifest) =>
        manifest.Installs
            .Where(record => string.Equals(record.Source.Provider, StrategyModId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.InstalledUtc)
            .FirstOrDefault();

    private static string ResolveInstallRoot(ModsFolderLayout layout, InstallRecord? previousRecord)
    {
        if (previousRecord is null || previousRecord.Files.Count == 0)
        {
            return layout.ModsFolderPath;
        }

        string sampleRelativePath = previousRecord.Files[0].RelativePath;
        bool livesInDisabledRoot = File.Exists(Path.Combine(layout.DisabledModsFolderPath, sampleRelativePath))
            && !File.Exists(Path.Combine(layout.ModsFolderPath, sampleRelativePath));

        return livesInDisabledRoot ? layout.DisabledModsFolderPath : layout.ModsFolderPath;
    }

    private void DeleteStaleFiles(string installRoot, InstallRecord? previousRecord, IReadOnlyList<InstallRecordFile> newFiles)
    {
        if (previousRecord is null)
        {
            return;
        }

        // The manifest is user-editable (adopted files, or a manually edited raw manifest via the
        // Settings-page viewer), so a record's RelativePath isn't guaranteed to stay under
        // installRoot the way ExtractArchive's zip-slip guard ensures for freshly extracted files.
        // Same containment check as ExtractArchive below, but skip-and-log instead of aborting the
        // whole update over one bad entry.
        string root = Path.GetFullPath(installRoot) + Path.DirectorySeparatorChar;
        HashSet<string> newPaths = new(newFiles.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (InstallRecordFile staleFile in previousRecord.Files.Where(file => !newPaths.Contains(file.RelativePath)))
        {
            string stalePath = Path.GetFullPath(Path.Combine(installRoot, staleFile.RelativePath));
            if (!stalePath.StartsWith(root, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Skipped deleting a stale WickedWhims file entry escaping {InstallRoot}: {RelativePath}",
                    installRoot,
                    staleFile.RelativePath);
                continue;
            }

            if (File.Exists(stalePath))
            {
                // Inferred from the previous install record, not chosen by the user — log every path so
                // a wrong record is traceable after the fact.
                File.Delete(stalePath);
                _logger.LogInformation("Deleted stale WickedWhims file from install {InstallId}: {DeletedPath}", previousRecord.InstallId, stalePath);
            }
        }
    }

    private static async Task SaveRecordAsync(
        ModsManifestService manifestService,
        ModsFolderLayout layout,
        ModsManifest manifest,
        InstallRecord? previousRecord,
        InstallRecord newRecord,
        CancellationToken cancellationToken)
    {
        HashSet<string> previousPaths = previousRecord is null
            ? []
            : new HashSet<string>(previousRecord.Files.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        HashSet<string> newPaths = new(newRecord.Files.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);

        List<ManifestFileEntry> files = [.. manifest.Files.Where(entry => !previousPaths.Contains(entry.RelativePath) && !newPaths.Contains(entry.RelativePath))];
        files.AddRange(newRecord.Files.Select(file => new ManifestFileEntry(file.RelativePath, "WickedWhims")));

        List<InstallRecord> installs = [.. manifest.Installs.Where(record => previousRecord is null || record.InstallId != previousRecord.InstallId), newRecord];

        await manifestService.SaveAsync(layout, manifest with { Files = files, Installs = installs }, cancellationToken);
    }

    /// <summary>
    /// Extracts every file entry of a zip archive into <paramref name="folder"/>, flat (WickedWhims
    /// ships a flat file set, not a subfoldered mod), guarding against zip-slip path traversal.
    /// </summary>
    private IReadOnlyList<InstallRecordFile> ExtractArchive(string folder, byte[] bytes)
    {
        Directory.CreateDirectory(folder);
        using ZipArchive archive = new(new MemoryStream(bytes), ZipArchiveMode.Read);

        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        List<InstallRecordFile> written = [];

        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                _logger.LogWarning("Rejected WickedWhims archive entry escaping {InstallRoot}: {EntryName}", folder, entry.FullName);
                throw new InvalidOperationException($"Unsafe archive path: {entry.FullName}");
            }

            string? directory = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"Could not resolve directory for archive entry '{entry.FullName}'.");
            }

            Directory.CreateDirectory(directory);
            entry.ExtractToFile(target, overwrite: true);

            InstallRecordFile writtenFile = new(
                Path.GetRelativePath(folder, target).Replace(Path.DirectorySeparatorChar, '/'),
                FileHashing.ComputeSha256(target),
                new FileInfo(target).Length);
            written.Add(writtenFile);

            _logger.LogDebug(
                "Extracted {EntryName} to {RelativePath} ({SizeBytes} bytes, sha256 {Sha256})",
                entry.FullName,
                writtenFile.RelativePath,
                writtenFile.SizeBytes,
                writtenFile.Sha256);
        }

        return written;
    }
}
