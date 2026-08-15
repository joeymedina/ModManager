using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsFileOperationsService(ModsFolderPathService pathService, ILogger<ModsFileOperationsService>? logger = null)
{
    private readonly ILogger<ModsFileOperationsService> _logger = logger ?? NullLogger<ModsFileOperationsService>.Instance;

    /// <summary>
    /// Moves each file between active and disabled roots to reach the target state. Continues past
    /// per-file failures (locked file, occupied destination) and returns them instead of throwing.
    /// Files already in the target state are treated as no-ops.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> MoveFilesForStateChangeAsync(IReadOnlyList<ModFile> files, ModFileState targetState, ModsFolderLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);

        string sourceRoot = targetState == ModFileState.Enabled
            ? layout.DisabledModsFolderPath
            : layout.ModsFolderPath;

        string targetRoot = targetState == ModFileState.Enabled
            ? layout.ModsFolderPath
            : layout.DisabledModsFolderPath;

        List<ModFileFailure> failures = [];

        foreach (ModFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.State == targetState)
            {
                continue;
            }

            string sourcePath = pathService.ResolveValidatedPath(sourceRoot, file.RelativePath);
            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning("Cannot {TargetState} {RelativePath}: source file not found at {SourcePath}", targetState, file.RelativePath, sourcePath);
                failures.Add(new ModFileFailure(file.RelativePath, "Source file not found."));
                continue;
            }

            string destinationPath = pathService.ResolveValidatedPath(targetRoot, file.RelativePath);
            if (File.Exists(destinationPath))
            {
                _logger.LogWarning("Cannot {TargetState} {RelativePath}: target already exists at {DestinationPath}", targetState, file.RelativePath, destinationPath);
                failures.Add(new ModFileFailure(file.RelativePath, "Target file already exists."));
                continue;
            }

            try
            {
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    _logger.LogWarning("Cannot {TargetState} {RelativePath}: unresolvable destination directory", targetState, file.RelativePath);
                    failures.Add(new ModFileFailure(file.RelativePath, "Could not resolve destination directory."));
                    continue;
                }

                Directory.CreateDirectory(destinationDirectory);
                File.Move(sourcePath, destinationPath, overwrite: false);
                _logger.LogInformation("Moved {RelativePath} to {TargetState} ({DestinationPath})", file.RelativePath, targetState, destinationPath);
                RemoveEmptyDirectories(Path.GetDirectoryName(sourcePath), sourceRoot);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to {TargetState} {RelativePath}", targetState, file.RelativePath);
                failures.Add(new ModFileFailure(file.RelativePath, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied while trying to {TargetState} {RelativePath}", targetState, file.RelativePath);
                failures.Add(new ModFileFailure(file.RelativePath, ex.Message));
            }
        }

        return Task.FromResult<IReadOnlyList<ModFileFailure>>(failures);
    }

    /// <summary>
    /// Deletes each file from whichever root(s) it exists in. A conflicted file exists in both roots
    /// and both copies are removed. Continues past per-file failures and returns them.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DeleteFilesAsync(IReadOnlyList<ModFile> files, ModsFolderLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);

        List<ModFileFailure> failures = [];

        foreach (ModFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool deletedAny = false;
            string? failureReason = null;

            foreach (string root in new[] { layout.ModsFolderPath, layout.DisabledModsFolderPath })
            {
                string path = pathService.ResolveValidatedPath(root, file.RelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    _logger.LogInformation("Deleted {DeletedPath}", path);
                    RemoveEmptyDirectories(Path.GetDirectoryName(path), root);
                    deletedAny = true;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete {DeletedPath}", path);
                    failureReason = ex.Message;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied while deleting {DeletedPath}", path);
                    failureReason = ex.Message;
                }
            }

            if (failureReason is not null)
            {
                failures.Add(new ModFileFailure(file.RelativePath, failureReason));
            }
            else if (!deletedAny)
            {
                _logger.LogWarning("Cannot delete {RelativePath}: not found under either root", file.RelativePath);
                failures.Add(new ModFileFailure(file.RelativePath, "File not found."));
            }
        }

        return Task.FromResult<IReadOnlyList<ModFileFailure>>(failures);
    }

    /// <summary>
    /// Deletes each of the given relative paths from <paramref name="installRoot"/> if present, then
    /// removes any directory left empty by the deletion. Used to prune the files an update or
    /// supersede no longer includes. A record's paths may come from a hand-edited manifest rather
    /// than a fresh extraction, so containment under <paramref name="installRoot"/> is re-checked
    /// here rather than assumed; a path that would escape it is skipped and reported instead of
    /// aborting the rest of the prune, matching this service's other bulk operations. A path that no
    /// longer exists is treated as already-clean, not a failure.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DeleteStalePathsAsync(string installRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(relativePaths);

        List<ModFileFailure> failures = [];

        foreach (string relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path;
            try
            {
                path = pathService.ResolveValidatedPath(installRoot, relativePath);
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning("Skipped deleting a stale file entry escaping {InstallRoot}: {RelativePath}", installRoot, relativePath);
                failures.Add(new ModFileFailure(relativePath, "Path escapes the install root."));
                continue;
            }

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                // Inferred from a previous install record, not chosen by the user in this action —
                // log every path so a wrong record is traceable after the fact.
                _logger.LogInformation("Deleted stale file {DeletedPath}", path);
                RemoveEmptyDirectories(Path.GetDirectoryName(path), installRoot);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete stale file {DeletedPath}", path);
                failures.Add(new ModFileFailure(relativePath, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied while deleting stale file {DeletedPath}", path);
                failures.Add(new ModFileFailure(relativePath, ex.Message));
            }
        }

        return Task.FromResult<IReadOnlyList<ModFileFailure>>(failures);
    }

    private void RemoveEmptyDirectories(string? directory, string stopAt)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stopAt))
        {
            return;
        }

        string normalizedStopAt = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, normalizedStopAt, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(current)
            && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            string? parent = Path.GetDirectoryName(current);
            Directory.Delete(current);
            _logger.LogInformation("Removed now-empty directory {DirectoryPath}", current);
            current = parent;
        }
    }
}
