using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsFileOperationsService(ModsFolderPathService pathService)
{
    /// <summary>
    /// Moves mod files between active and disabled roots based on target state.
    /// </summary>
    public Task MoveModFilesForStateChangeAsync(ManagedMod mod, ModFileState targetState, ModsFolderLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(layout);

        string sourceRoot = targetState == ModFileState.Enabled
            ? layout.DisabledModsFolderPath
            : layout.ModsFolderPath;

        string targetRoot = targetState == ModFileState.Enabled
            ? layout.ModsFolderPath
            : layout.DisabledModsFolderPath;

        foreach (ManagedModFile file in mod.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.State == targetState)
            {
                continue;
            }

            string sourcePath = pathService.ResolveValidatedPath(sourceRoot, file.RelativePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Could not find source mod file '{file.RelativePath}'.", sourcePath);
            }

            string destinationPath = pathService.ResolveValidatedPath(targetRoot, file.RelativePath);
            if (File.Exists(destinationPath))
            {
                throw new InvalidOperationException($"Cannot move '{file.RelativePath}' because target file already exists.");
            }

            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException($"Could not resolve destination directory for '{file.RelativePath}'.");
            }

            Directory.CreateDirectory(destinationDirectory);
            File.Move(sourcePath, destinationPath, overwrite: false);
            RemoveEmptyDirectories(Path.GetDirectoryName(sourcePath), sourceRoot);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes all files associated with a mod from active and disabled roots.
    /// </summary>
    public Task DeleteModFilesAsync(ManagedMod mod, ModsFolderLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(layout);

        foreach (ManagedModFile file in mod.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string root = file.State == ModFileState.Enabled
                ? layout.ModsFolderPath
                : layout.DisabledModsFolderPath;

            string sourcePath = pathService.ResolveValidatedPath(root, file.RelativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            File.Delete(sourcePath);
            RemoveEmptyDirectories(Path.GetDirectoryName(sourcePath), root);
        }

        return Task.CompletedTask;
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
