using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<ModsFolderService> _logger;

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
        ModsManifestService manifestService,
        ILogger<ModsFolderService>? logger = null)
    {
        _pathService = pathService;
        _discoveryService = discoveryService;
        _fileOperationsService = fileOperationsService;
        _manifestService = manifestService;
        _logger = logger ?? NullLogger<ModsFolderService>.Instance;
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
    /// (display name, group, and the owning install's id/version/installed date/provider) onto them.
    /// Never writes and never creates either root folder.
    /// </summary>
    public async Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        if (manifest.Files.Count == 0 && manifest.Installs.Count == 0)
        {
            _logger.LogDebug("Discovered {FileCount} file(s) in {ModsFolder}; no manifest metadata to layer on", discovered.Count, layout.ModsFolderPath);
            return discovered;
        }

        _logger.LogDebug(
            "Discovered {FileCount} file(s) in {ModsFolder}; layering {EntryCount} manifest entries from {InstallCount} install(s)",
            discovered.Count,
            layout.ModsFolderPath,
            manifest.Files.Count,
            manifest.Installs.Count);

        Dictionary<string, ManifestFileEntry> entriesByPath = manifest.Files
            .ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, InstallRecord> recordByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstallRecord record in manifest.Installs)
        {
            foreach (InstallRecordFile file in record.Files)
            {
                recordByPath[file.RelativePath] = record;
            }
        }

        return [.. discovered.Select(file => ApplyManifest(file, entriesByPath, recordByPath))];
    }

    private static ModFile ApplyManifest(
        ModFile file,
        IReadOnlyDictionary<string, ManifestFileEntry> entriesByPath,
        IReadOnlyDictionary<string, InstallRecord> recordByPath)
    {
        ManifestFileEntry? entry = entriesByPath.GetValueOrDefault(file.RelativePath);
        InstallRecord? record = recordByPath.GetValueOrDefault(file.RelativePath);

        if (entry is null && record is null)
        {
            return file;
        }

        return file with
        {
            DisplayName = entry?.DisplayName,
            GroupId = entry?.GroupId,
            InstallId = record?.InstallId,
            Version = record?.Version,
            InstalledUtc = record?.InstalledUtc,
            Provider = record?.Source.Provider,
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

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths, "Delete");

        _logger.LogInformation("Deleting {MatchedCount} of {RequestedCount} requested file(s) from {ModsFolder}", matched.Count, relativePaths.Count, layout.ModsFolderPath);

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

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths, targetState.ToString());

        List<ModFile> conflicted = [.. matched.Where(file => file.IsConflicted)];
        foreach (ModFile file in conflicted)
        {
            _logger.LogWarning("Cannot set {RelativePath} to {TargetState}: it exists under both roots and must be resolved first", file.RelativePath, targetState);
        }

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

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths, "Adopt");
        if (failures.Count > 0)
        {
            string missing = string.Join(", ", failures.Select(failure => failure.RelativePath));
            _logger.LogWarning("Adopt of \"{DisplayName}\" abandoned: {MissingCount} requested file(s) not found", displayName, failures.Count);
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

        _logger.LogInformation(
            "Adopted {FileCount} file(s) as \"{DisplayName}\" (install {InstallId}, version {Version})",
            files.Count,
            displayName,
            record.InstallId,
            version ?? "unspecified");

        return ArchiveInstallResult<InstallRecord>.Ok(record);
    }

    /// <summary>
    /// Loads the manifest's group definitions, including members whose path no longer resolves to a
    /// discovered file. Never writes.
    /// </summary>
    public async Task<IReadOnlyList<ModGroup>> LoadGroupsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        return manifest.Groups;
    }

    /// <summary>
    /// Adds the given files to a group, reusing an existing group with a case-insensitive matching
    /// name or minting a new one. Since a file belongs to at most one group, each path is removed from
    /// whatever group it previously belonged to, pruning that group if left empty. All-or-nothing:
    /// fails if any path can't be found under either root.
    /// </summary>
    public async Task<ArchiveInstallResult<ModGroup>> AddToGroupAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        if (relativePaths.Count == 0)
        {
            return ArchiveInstallResult<ModGroup>.Fail("Select at least one file first.");
        }

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths, "AddToGroup");
        if (failures.Count > 0)
        {
            string missing = string.Join(", ", failures.Select(failure => failure.RelativePath));
            _logger.LogWarning("Add to group \"{GroupName}\" abandoned: {MissingCount} requested file(s) not found", groupName, failures.Count);
            return ArchiveInstallResult<ModGroup>.Fail($"File(s) not found: {missing}");
        }

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);

        ModGroup? existingGroup = manifest.Groups
            .FirstOrDefault(group => string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
        string groupId = existingGroup?.GroupId ?? Guid.NewGuid().ToString("N");
        string resolvedName = existingGroup?.Name ?? groupName;

        HashSet<string> newMemberPaths = new(matched.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);

        List<ModGroup> groups = [];
        foreach (ModGroup group in manifest.Groups)
        {
            if (string.Equals(group.GroupId, groupId, StringComparison.Ordinal))
            {
                continue;
            }

            List<string> remainingMembers = [.. group.Members.Where(member => !newMemberPaths.Contains(member))];
            if (remainingMembers.Count > 0)
            {
                groups.Add(group with { Members = remainingMembers });
            }
        }

        List<string> mergedMembers = [.. (existingGroup?.Members ?? []).Union(newMemberPaths, StringComparer.OrdinalIgnoreCase)];
        ModGroup updatedGroup = new(groupId, resolvedName, mergedMembers);
        groups.Add(updatedGroup);

        Dictionary<string, ManifestFileEntry> entriesByPath = manifest.Files
            .ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        List<ManifestFileEntry> files = [];
        foreach (ManifestFileEntry entry in manifest.Files)
        {
            files.Add(newMemberPaths.Contains(entry.RelativePath) ? entry with { GroupId = groupId } : entry);
        }

        foreach (string path in newMemberPaths.Where(path => !entriesByPath.ContainsKey(path)))
        {
            files.Add(new ManifestFileEntry(path, GroupId: groupId));
        }

        ModsManifest updated = manifest with { Files = files, Groups = groups };
        await _manifestService.SaveAsync(layout, updated, cancellationToken);

        return ArchiveInstallResult<ModGroup>.Ok(updatedGroup);
    }

    /// <summary>
    /// Removes the given paths from whatever group they belong to. A group left with no members is
    /// dropped, and a manifest entry left with no metadata at all is dropped too.
    /// </summary>
    public async Task RemoveFromGroupAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (relativePaths.Count == 0)
        {
            return;
        }

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);

        HashSet<string> targetPaths = new(relativePaths, StringComparer.OrdinalIgnoreCase);

        List<ModGroup> groups = [];
        foreach (ModGroup group in manifest.Groups)
        {
            List<string> remainingMembers = [.. group.Members.Where(member => !targetPaths.Contains(member))];
            if (remainingMembers.Count > 0)
            {
                groups.Add(group with { Members = remainingMembers });
            }
        }

        List<ManifestFileEntry> files = [.. manifest.Files
            .Select(entry => targetPaths.Contains(entry.RelativePath) ? entry with { GroupId = null } : entry)
            .Where(entry => entry.DisplayName is not null || entry.GroupId is not null || entry.Notes is not null)];

        await _manifestService.SaveAsync(layout, manifest with { Files = files, Groups = groups }, cancellationToken);
    }

    /// <summary>
    /// Resolves requested relative paths against what discovery actually found. Every caller routes
    /// through here, so this is the one place a "file not found" needs logging.
    /// </summary>
    private (List<ModFile> Matched, List<ModFileFailure> Failures) MatchRequestedPaths(
        IReadOnlyList<ModFile> discovered,
        IReadOnlyList<string> relativePaths,
        string operation)
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
                _logger.LogWarning("{Operation}: requested file {RelativePath} was not found under either root", operation, path);
                failures.Add(new ModFileFailure(path, "File not found."));
            }
        }

        return (matched, failures);
    }
}
