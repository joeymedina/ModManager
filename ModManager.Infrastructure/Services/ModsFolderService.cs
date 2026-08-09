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

    public ModsFolderService():
        this(
            new ModsFolderPathService(),
            new ModsDiscoveryService(),
            new ModsFileOperationsService(new ModsFolderPathService()))
    {
    }

    public ModsFolderService(
        ModsFolderPathService pathService,
        ModsDiscoveryService discoveryService,
        ModsFileOperationsService fileOperationsService)
    {
        _pathService = pathService;
        _discoveryService = discoveryService;
        _fileOperationsService = fileOperationsService;
    }

    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        return _pathService.GetLayout(modsFolderPath);
    }

    /// <summary>
    /// Discovers mod files from both active and disabled folders. Never writes and never creates
    /// either root folder.
    /// </summary>
    public Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ModsFolderLayout layout = _pathService.GetLayout(modsFolderPath);
        IReadOnlyList<ModFile> files = _discoveryService.DiscoverFiles(layout);
        return Task.FromResult(files);
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
