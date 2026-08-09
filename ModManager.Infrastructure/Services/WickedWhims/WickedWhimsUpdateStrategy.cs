using System.IO.Compression;
using System.Security.Cryptography;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsUpdateStrategy(
    ModsFolderPathService pathService,
    ModsManifestService manifestService,
    WickedWhimsVersionDetector versionDetector,
    WickedWhimsReleaseClient releaseClient) : IModUpdateStrategy
{
    public const string StrategyModId = "wickedwhims";

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

        if (comparison < 0 && request.DownloadIfUpdateAvailable)
        {
            WickedWhimsDownload download = await releaseClient.DownloadLatestArchiveAsync(cancellationToken);
            IReadOnlyList<InstallRecordFile> newFiles = ExtractArchive(installRoot, download.Bytes);

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

    private static void DeleteStaleFiles(string installRoot, InstallRecord? previousRecord, IReadOnlyList<InstallRecordFile> newFiles)
    {
        if (previousRecord is null)
        {
            return;
        }

        HashSet<string> newPaths = new(newFiles.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (InstallRecordFile staleFile in previousRecord.Files.Where(file => !newPaths.Contains(file.RelativePath)))
        {
            string stalePath = Path.Combine(installRoot, staleFile.RelativePath);
            if (File.Exists(stalePath))
            {
                File.Delete(stalePath);
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
    private static IReadOnlyList<InstallRecordFile> ExtractArchive(string folder, byte[] bytes)
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
                throw new InvalidOperationException($"Unsafe archive path: {entry.FullName}");
            }

            string? directory = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"Could not resolve directory for archive entry '{entry.FullName}'.");
            }

            Directory.CreateDirectory(directory);
            entry.ExtractToFile(target, overwrite: true);

            written.Add(new InstallRecordFile(
                Path.GetRelativePath(folder, target).Replace(Path.DirectorySeparatorChar, '/'),
                ComputeSha256(target),
                new FileInfo(target).Length));
        }

        return written;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
