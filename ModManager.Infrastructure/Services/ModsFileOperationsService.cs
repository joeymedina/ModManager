using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsFileOperationsService(ModsFolderPathService pathService)
{
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
                failures.Add(new ModFileFailure(file.RelativePath, "Source file not found."));
                continue;
            }

            string destinationPath = pathService.ResolveValidatedPath(targetRoot, file.RelativePath);
            if (File.Exists(destinationPath))
            {
                failures.Add(new ModFileFailure(file.RelativePath, "Target file already exists."));
                continue;
            }

            try
            {
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    failures.Add(new ModFileFailure(file.RelativePath, "Could not resolve destination directory."));
                    continue;
                }

                Directory.CreateDirectory(destinationDirectory);
                File.Move(sourcePath, destinationPath, overwrite: false);
                RemoveEmptyDirectories(Path.GetDirectoryName(sourcePath), sourceRoot);
            }
            catch (IOException ex)
            {
                failures.Add(new ModFileFailure(file.RelativePath, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
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
                    RemoveEmptyDirectories(Path.GetDirectoryName(path), root);
                    deletedAny = true;
                }
                catch (IOException ex)
                {
                    failureReason = ex.Message;
                }
                catch (UnauthorizedAccessException ex)
                {
                    failureReason = ex.Message;
                }
            }

            if (failureReason is not null)
            {
                failures.Add(new ModFileFailure(file.RelativePath, failureReason));
            }
            else if (!deletedAny)
            {
                failures.Add(new ModFileFailure(file.RelativePath, "File not found."));
            }
        }

        return Task.FromResult<IReadOnlyList<ModFileFailure>>(failures);
    }

    private static void RemoveEmptyDirectories(string? directory, string stopAt)
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
            current = parent;
        }
    }
}
