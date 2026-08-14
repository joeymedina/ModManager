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
    private readonly IReadOnlyList<IModSiteStrategy> _siteStrategies;
    private readonly ILogger<ModsFolderService> _logger;

    public ModsFolderService():
        this(
            new ModsFolderPathService(),
            new ModsDiscoveryService(),
            new ModsFileOperationsService(new ModsFolderPathService()),
            new ModsManifestService(),
            [])
    {
    }

    public ModsFolderService(
        ModsFolderPathService pathService,
        ModsDiscoveryService discoveryService,
        ModsFileOperationsService fileOperationsService,
        ModsManifestService manifestService,
        IEnumerable<IModSiteStrategy>? siteStrategies = null,
        ILogger<ModsFolderService>? logger = null)
    {
        _pathService = pathService;
        _discoveryService = discoveryService;
        _fileOperationsService = fileOperationsService;
        _manifestService = manifestService;
        _siteStrategies = [.. siteStrategies ?? []];
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
            Category = entry?.Category,
            InstallId = record?.InstallId,
            Version = record?.Version,
            InstalledUtc = record?.InstalledUtc,
            Provider = record?.Source.Provider,
            ModPageUrl = record?.Source.ModPageUrl,
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

        // This reason is shown verbatim to the user, so it names the cause and the fix rather than
        // just the internal state name.
        failures.AddRange(conflicted.Select(file => new ModFileFailure(
            file.RelativePath,
            "a copy exists in both the Mods and Mods.Disabled folders — delete the one you don't want, then try again")));

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
            [],
            ResolveTracking(modPageUrl, version, displayName, files));

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        HashSet<string> adoptedPaths = [.. files.Select(file => file.RelativePath)];

        // Re-adopting the same file(s) is how a mistake (wrong URL, wrong version) gets corrected, so
        // any existing record sharing a path with this adoption is replaced rather than left behind as
        // a stale duplicate — the same append-only bug this fixed for ArchiveInstallService.InstallAsync
        // via its `supersedes` parameter. "Any shared path" rather than an exact-set match also means
        // this self-heals a manifest that already has duplicates from before this fix existed.
        HashSet<string> supersededInstallIds = [.. manifest.Installs
            .Where(existing => existing.Files.Any(file => adoptedPaths.Contains(file.RelativePath)))
            .Select(existing => existing.InstallId)];

        Dictionary<string, ManifestFileEntry> existingByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestFileEntry entry in manifest.Files)
        {
            existingByPath[entry.RelativePath] = entry;
        }

        List<ManifestFileEntry> manifestFiles = [.. manifest.Files.Where(entry => !adoptedPaths.Contains(entry.RelativePath))];
        manifestFiles.AddRange(files.Select(file =>
        {
            // Carries forward GroupId/Category/Notes a file already had — from before it was ever
            // adopted, or from a previous adopt — rather than silently wiping them, the same fix
            // ArchiveInstallService.PersistRecordAsync got for the same latent bug.
            existingByPath.TryGetValue(file.RelativePath, out ManifestFileEntry? existing);
            return new ManifestFileEntry(file.RelativePath, displayName, existing?.GroupId, existing?.Notes, existing?.Category);
        }));

        List<InstallRecord> installs = [.. manifest.Installs.Where(existing => !supersededInstallIds.Contains(existing.InstallId)), record];
        ModsManifest updated = manifest with { Files = manifestFiles, Installs = installs };
        await _manifestService.SaveAsync(layout, updated, cancellationToken);

        if (supersededInstallIds.Count > 0)
        {
            _logger.LogInformation(
                "Adopt of \"{DisplayName}\" superseded {Count} previous install record(s) covering the same path(s)",
                displayName,
                supersededInstallIds.Count);
        }

        _logger.LogInformation(
            "Adopted {FileCount} file(s) as \"{DisplayName}\" (install {InstallId}, version {Version})",
            files.Count,
            displayName,
            record.InstallId,
            version ?? "unspecified");

        return ArchiveInstallResult<InstallRecord>.Ok(record);
    }

    /// <summary>
    /// Builds the update-tracking baseline for a freshly adopted record. Adoption's existing "mod
    /// page" field doubles as the tracking URL — no separate "link to a site" action is needed. Only
    /// set when a registered strategy's <see cref="IModSiteStrategy.Hosts"/> actually matches the URL:
    /// a user can paste a mod page from any site during adoption, most of which this app has no
    /// strategy for, and tracking one anyway would leave a permanently-`Indeterminate` "no strategy
    /// registered" row cluttering the Updates page for a site that was never checkable to begin with.
    /// The matched strategy's canonical <see cref="IModSiteStrategy.SiteKey"/> is used (so
    /// <c>www.</c> and bare-domain variants land on the same key), and its mod key is resolved
    /// immediately if possible — left unresolved, to retry on the next check, otherwise.
    /// </summary>
    private UpdateTracking? ResolveTracking(string? modPageUrl, string? version, string displayName, IReadOnlyList<InstallRecordFile> files)
    {
        if (string.IsNullOrWhiteSpace(modPageUrl) || !Uri.TryCreate(modPageUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        IModSiteStrategy? strategy = _siteStrategies.FirstOrDefault(
            candidate => candidate.Hosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)));

        if (strategy is null)
        {
            return null;
        }

        SiteModKey? resolvedKey = strategy.TryResolveModKey(new ModKeyHints(modPageUrl, null, displayName, [.. files.Select(file => file.RelativePath)]));
        return new UpdateTracking(strategy.SiteKey, resolvedKey?.Value, modPageUrl, version, null, DateTime.UtcNow);
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
            .Where(entry => entry.DisplayName is not null || entry.GroupId is not null || entry.Notes is not null || entry.Category is not null)];

        await _manifestService.SaveAsync(layout, manifest with { Files = files, Groups = groups }, cancellationToken);
    }

    /// <summary>
    /// Sets (or clears, when <paramref name="category"/> is null or blank) the category on the given
    /// files. Unlike groups, a category is a plain field per file — no separate list, no id-minting.
    /// All-or-nothing: fails if any path can't be found under either root.
    /// </summary>
    public async Task<ArchiveInstallResult<string?>> SetCategoryAsync(
        string modsFolderPath,
        IReadOnlyList<string> relativePaths,
        string? category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (relativePaths.Count == 0)
        {
            return ArchiveInstallResult<string?>.Fail("Select at least one file first.");
        }

        string? normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> discovered = _discoveryService.DiscoverFiles(layout);

        (List<ModFile> matched, List<ModFileFailure> failures) = MatchRequestedPaths(discovered, relativePaths, "SetCategory");
        if (failures.Count > 0)
        {
            string missing = string.Join(", ", failures.Select(failure => failure.RelativePath));
            _logger.LogWarning("Set category abandoned: {MissingCount} requested file(s) not found", failures.Count);
            return ArchiveInstallResult<string?>.Fail($"File(s) not found: {missing}");
        }

        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);
        HashSet<string> targetPaths = new(matched.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ManifestFileEntry> entriesByPath = manifest.Files
            .ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<ManifestFileEntry> files = [.. manifest.Files
            .Select(entry => targetPaths.Contains(entry.RelativePath) ? entry with { Category = normalizedCategory } : entry)
            .Where(entry => entry.DisplayName is not null || entry.GroupId is not null || entry.Notes is not null || entry.Category is not null)];

        if (normalizedCategory is not null)
        {
            files.AddRange(targetPaths
                .Where(path => !entriesByPath.ContainsKey(path))
                .Select(path => new ManifestFileEntry(path, Category: normalizedCategory)));
        }

        await _manifestService.SaveAsync(layout, manifest with { Files = files }, cancellationToken);
        return ArchiveInstallResult<string?>.Ok(normalizedCategory);
    }

    /// <summary>
    /// Loads the full manifest for display. Never writes.
    /// </summary>
    public async Task<ModsManifest> LoadManifestAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        return await _manifestService.LoadAsync(layout, cancellationToken);
    }

    /// <summary>
    /// Reads the manifest file's raw JSON text, for display verbatim.
    /// </summary>
    public async Task<ManifestRawContent> ReadManifestRawAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        string path = ModsManifestService.GetManifestPath(layout);
        string? raw = await _manifestService.ReadRawAsync(layout, cancellationToken);
        return new ManifestRawContent(path, raw is not null, raw);
    }

    /// <summary>
    /// Parses and saves manually edited manifest JSON. Rejects (without writing) JSON that doesn't
    /// parse or uses an unsupported schema version.
    /// </summary>
    public async Task<ArchiveInstallResult<ModsManifest>> SaveManifestRawAsync(string modsFolderPath, string rawJson, CancellationToken cancellationToken = default)
    {
        if (!ModsManifestService.TryParseRaw(rawJson, out ModsManifest? parsed, out string? error))
        {
            _logger.LogWarning("Rejected a manually edited manifest for {ModsFolder}: {Reason}", modsFolderPath, error);
            return ArchiveInstallResult<ModsManifest>.Fail($"That JSON isn't a valid manifest: {error}.");
        }

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        await _manifestService.SaveAsync(layout, parsed!, cancellationToken);
        _logger.LogInformation("Saved a manually edited manifest for {ModsFolder}", modsFolderPath);
        return ArchiveInstallResult<ModsManifest>.Ok(parsed!);
    }

    /// <summary>
    /// Renames an install's on-disk folder and rewrites every stored reference to its old path — the
    /// record's own files, matching manifest file entries, and group membership — as one operation.
    /// See <see cref="IModsFolderRepository.RenameInstallFolderAsync"/> for the collision and
    /// rollback behavior.
    /// </summary>
    public async Task<ArchiveInstallResult<InstallRecord>> RenameInstallFolderAsync(
        string modsFolderPath,
        string installId,
        string desiredFolderName,
        CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);

        InstallRecord? record = manifest.Installs.FirstOrDefault(candidate => candidate.InstallId == installId);
        if (record is null || record.Files.Count == 0)
        {
            _logger.LogWarning("Rename abandoned: no install {InstallId} with files found in {ModsFolder}", installId, modsFolderPath);
            return ArchiveInstallResult<InstallRecord>.Fail("Could not find that install.");
        }

        string oldFolderName = record.Files[0].RelativePath.Split('/', 2)[0];

        // Checked before deduping: the mod's own current folder is not a "collision" to dedupe
        // against, since a rename to the name it already has is a no-op, not a move onto itself.
        if (string.Equals(oldFolderName, ModsFolderPathService.SanitizeFolderName(desiredFolderName), StringComparison.Ordinal))
        {
            return ArchiveInstallResult<InstallRecord>.Ok(record);
        }

        string newFolderName = ModsFolderPathService.ResolveDedupedFolderName(desiredFolderName, layout.ModsFolderPath, layout.DisabledModsFolderPath);

        // A record's files live under exactly one root, but check both — a mixed-state mod (some
        // files individually disabled) can have the same folder present under both.
        List<(string Root, string OldPath, string NewPath)> moves = [.. new[] { layout.ModsFolderPath, layout.DisabledModsFolderPath }
            .Where(root => Directory.Exists(Path.Combine(root, oldFolderName)))
            .Select(root => (root, Path.Combine(root, oldFolderName), Path.Combine(root, newFolderName)))];

        if (moves.Count == 0)
        {
            _logger.LogWarning("Rename abandoned: install {InstallId}'s folder \"{OldFolderName}\" was not found under either root", installId, oldFolderName);
            return ArchiveInstallResult<InstallRecord>.Fail("Could not find the mod's folder on disk.");
        }

        List<(string Root, string OldPath, string NewPath)> moved = [];
        try
        {
            foreach ((string root, string oldPath, string newPath) in moves)
            {
                Directory.Move(oldPath, newPath);
                moved.Add((root, oldPath, newPath));
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to rename install {InstallId}'s folder from \"{OldFolderName}\" to \"{NewFolderName}\"", installId, oldFolderName, newFolderName);
            RollBackFolderMoves(moved);
            return ArchiveInstallResult<InstallRecord>.Fail(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied renaming install {InstallId}'s folder from \"{OldFolderName}\" to \"{NewFolderName}\"", installId, oldFolderName, newFolderName);
            RollBackFolderMoves(moved);
            return ArchiveInstallResult<InstallRecord>.Fail(ex.Message);
        }

        string oldPrefix = oldFolderName + "/";
        string newPrefix = newFolderName + "/";

        InstallRecord renamedRecord = record with { Files = [.. record.Files.Select(file => file with { RelativePath = RewritePrefix(file.RelativePath, oldPrefix, newPrefix) })] };
        List<InstallRecord> installs = [.. manifest.Installs.Where(candidate => candidate.InstallId != installId), renamedRecord];
        List<ManifestFileEntry> files = [.. manifest.Files.Select(entry => entry with { RelativePath = RewritePrefix(entry.RelativePath, oldPrefix, newPrefix) })];
        List<ModGroup> groups = [.. manifest.Groups.Select(group => group with { Members = [.. group.Members.Select(member => RewritePrefix(member, oldPrefix, newPrefix))] })];

        try
        {
            await _manifestService.SaveAsync(layout, manifest with { Files = files, Groups = groups, Installs = installs }, cancellationToken);
        }
        catch
        {
            // Disk and manifest must never disagree about where a mod lives — a save failure here
            // would otherwise orphan every path the manifest still references under the old name.
            RollBackFolderMoves(moved);
            throw;
        }

        _logger.LogInformation("Renamed install {InstallId}'s folder from \"{OldFolderName}\" to \"{NewFolderName}\"", installId, oldFolderName, newFolderName);
        return ArchiveInstallResult<InstallRecord>.Ok(renamedRecord);
    }

    /// <summary>
    /// Replaces an install record's <see cref="UpdateTracking"/> baseline. See
    /// <see cref="IModsFolderRepository.UpdateInstallTrackingAsync"/> for what calls this.
    /// </summary>
    public async Task<ArchiveInstallResult<InstallRecord>> UpdateInstallTrackingAsync(
        string modsFolderPath,
        string installId,
        UpdateTracking tracking,
        CancellationToken cancellationToken = default)
    {
        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        ModsManifest manifest = await _manifestService.LoadAsync(layout, cancellationToken);

        InstallRecord? record = manifest.Installs.FirstOrDefault(candidate => candidate.InstallId == installId);
        if (record is null)
        {
            _logger.LogWarning("Could not update tracking: no install {InstallId} found in {ModsFolder}", installId, modsFolderPath);
            return ArchiveInstallResult<InstallRecord>.Fail("Could not find that install.");
        }

        InstallRecord updated = record with { Tracking = tracking };
        List<InstallRecord> installs = [.. manifest.Installs.Where(candidate => candidate.InstallId != installId), updated];
        await _manifestService.SaveAsync(layout, manifest with { Installs = installs }, cancellationToken);

        _logger.LogInformation("Updated tracking for install {InstallId}: site {SiteKey}, mod key {SiteModKey}", installId, tracking.SiteKey, tracking.SiteModKey ?? "(unresolved)");
        return ArchiveInstallResult<InstallRecord>.Ok(updated);
    }

    private static string RewritePrefix(string relativePath, string oldPrefix, string newPrefix) =>
        relativePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
            ? newPrefix + relativePath[oldPrefix.Length..]
            : relativePath;

    private void RollBackFolderMoves(List<(string Root, string OldPath, string NewPath)> moved)
    {
        foreach ((string root, string oldPath, string newPath) in moved)
        {
            if (Directory.Exists(newPath) && !Directory.Exists(oldPath))
            {
                Directory.Move(newPath, oldPath);
                _logger.LogInformation("Rolled back folder rename in {Root}: {NewPath} -> {OldPath}", root, newPath, oldPath);
            }
        }
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
