using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// File-system repository for loading and managing Sims 4 mod files in Mods and Mods.Disabled.
/// </summary>
public sealed class ModsFolderService : IModsFolderRepository
{
    private readonly ModsFolderPathService _pathService;
    private readonly ModsDiscoveryService _discoveryService;
    private readonly ModsFileOperationsService _fileOperationsService;
    private readonly ModsManifestService _manifestService;

    public ModsFolderService():
        this(
            new ModsFolderPathService(),
            new ModsDiscoveryService(),
            new ModsFileOperationsService(new ModsFolderPathService()),
            new ModsManifestService())
    {
    }

    public ModsFolderService(
        ModsFolderPathService pathService,
        ModsDiscoveryService discoveryService,
        ModsFileOperationsService fileOperationsService,
        ModsManifestService manifestService)
    {
        _pathService = pathService;
        _discoveryService = discoveryService;
        _fileOperationsService = fileOperationsService;
        _manifestService = manifestService;
    }

    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        return _pathService.GetLayout(modsFolderPath);
    }

    /// <summary>
    /// Discovers mod files from both active and disabled folders and layers manifest metadata
    /// (display name, group, install id) onto them. Never writes and never creates either root
    /// folder.
    /// </summary>
    public async Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        if (manifest.Files.Count == 0 && manifest.Installs.Count == 0)
        {
            return discovered;
        }

        Dictionary<string, ManifestFileEntry> entriesByPath = manifest.Files
            .ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> installIdByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstallRecord record in manifest.Installs)
        {
            foreach (InstallRecordFile file in record.Files)
            {
                installIdByPath[file.RelativePath] = record.InstallId;
            }
        }

        return [.. discovered.Select(file => ApplyManifest(file, entriesByPath, installIdByPath))];
    }

    private static ModFile ApplyManifest(
        ModFile file,
        IReadOnlyDictionary<string, ManifestFileEntry> entriesByPath,
        IReadOnlyDictionary<string, string> installIdByPath)
    {
        ManifestFileEntry? entry = entriesByPath.GetValueOrDefault(file.RelativePath);
        string? installId = installIdByPath.GetValueOrDefault(file.RelativePath);

        if (entry is null && installId is null)
        {
            return file;
        }

        return file with
        {
            DisplayName = entry?.DisplayName,
            GroupId = entry?.GroupId,
            InstallId = installId,
        };
    }

    /// <summary>
    /// Moves the given files into the active mods folder.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        return ChangeStateAsync(modsFolderPath, relativePaths, ModFileState.Enabled, cancellationToken);
    }

    /// <summary>
    /// Moves the given files into the disabled mods folder.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        return ChangeStateAsync(modsFolderPath, relativePaths, ModFileState.Disabled, cancellationToken);
    }

    /// <summary>
    /// Deletes the given files from active and/or disabled folders. A conflicted path is removed
    /// from both.
    /// </summary>
    public async Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths);

        IReadOnlyList<ModFileFailure> deleteFailures = await _fileOperationsService.DeleteFilesAsync(matched, layout, cancellationToken);
        failures.AddRange(deleteFailures);
        return failures;
    }

    private async Task<IReadOnlyList<ModFileFailure>> ChangeStateAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, ModFileState targetState, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths);

        List<ModFile> conflicted = [.. matched.Where(file => file.IsConflicted)];
        failures.AddRange(conflicted.Select(file => new ModFileFailure(file.RelativePath, "File is conflicted; resolve before changing state.")));

        List<ModFile> actionable = [.. matched.Where(file => !file.IsConflicted)];
        IReadOnlyList<ModFileFailure> moveFailures = await _fileOperationsService.MoveFilesForStateChangeAsync(actionable, targetState, layout, cancellationToken);
        failures.AddRange(moveFailures);

        return failures;
    }

    /// <summary>
    /// Links already-discovered files to a source by writing an InstallRecord that covers their
    /// current paths. Metadata only — never moves or extracts anything. All-or-nothing: fails if any
    /// path can't be found under either root, rather than adopting the rest.
    /// </summary>
    public async Task<ArchiveInstallResult<InstallRecord>> AdoptAsync(
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

        if (relativePaths.Count == 0)
        {
            return ArchiveInstallResult<InstallRecord>.Fail("Select at least one file first.");
        }

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths);
        if (failures.Count > 0)
        {
            string missing = string.Join(", ", failures.Select(failure => failure.RelativePath));
            return ArchiveInstallResult<InstallRecord>.Fail($"File(s) not found: {missing}");
        }

        List<InstallRecordFile> files = [];
        foreach (ModFile file in matched)
        {
            string root = file.State == ModFileState.Enabled ? layout.ModsFolderPath : layout.DisabledModsFolderPath;
            string fullPath = _pathService.ResolveValidatedPath(root, file.RelativePath);
            files.Add(new InstallRecordFile(file.RelativePath, FileHashing.ComputeSha256(fullPath), file.SizeBytes));
        }

        InstallRecord record = new(
            Guid.NewGuid().ToString("N"),
            new InstallSource("adopted", modPageUrl, null),
            version,
            DateTime.UtcNow,
            null,
            files,
            []);

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        HashSet<string> adoptedPaths = [.. files.Select(file => file.RelativePath)];
        List<ManifestFileEntry> manifestFiles = [.. manifest.Files.Where(entry => !adoptedPaths.Contains(entry.RelativePath))];
        manifestFiles.AddRange(files.Select(file => new ManifestFileEntry(file.RelativePath, displayName)));

        ModsManifest updated = manifest with { Files = manifestFiles, Installs = [.. manifest.Installs, record] };
        await _manifestService.SaveAsync(layout, updated, cancellationToken);

        return ArchiveInstallResult<InstallRecord>.Ok(record);
    }

    private static (List<ModFile> Matched, List<ModFileFailure> Failures) MatchRequestedPaths(IReadOnlyList<ModFile> discovered, IReadOnlyList<string> relativePaths)
    {
        Dictionary<string, ModFile> byPath = discovered.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<ModFile> matched = [];
        List<ModFileFailure> failures = [];

        foreach (string path in relativePaths)
        {
            if (byPath.TryGetValue(path, out ModFile? file))
            {
                matched.Add(file);
            }
            else
            {
                failures.Add(new ModFileFailure(path, "File not found."));
            }
        }

        return (matched, failures);
    }
}
