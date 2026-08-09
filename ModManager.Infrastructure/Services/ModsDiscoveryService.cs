using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsDiscoveryService
{
    private static readonly HashSet<string> SupportedExtensions = [".package", ".ts4script"];

    /// <summary>
    /// Discovers mod files from active and disabled folders as a flat, sorted list. Pure: never writes.
    /// A relative path present under both roots yields one conflicted, enabled row.
    /// </summary>
    public IReadOnlyList<ModFile> DiscoverFiles(ModsFolderLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Dictionary<string, ModFile> byPath = new(StringComparer.OrdinalIgnoreCase);

        foreach (ModFile file in EnumerateModFiles(layout.ModsFolderPath, ModFileState.Enabled))
        {
            byPath[file.RelativePath] = file;
        }

        foreach (ModFile file in EnumerateModFiles(layout.DisabledModsFolderPath, ModFileState.Disabled))
        {
            byPath[file.RelativePath] = byPath.TryGetValue(file.RelativePath, out ModFile? existing)
                ? existing with { IsConflicted = true }
                : file;
        }

        return [.. byPath.Values.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<ModFile> EnumerateModFiles(string root, ModFileState state)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (FileInfo fileInfo in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (!SupportedExtensions.Contains(fileInfo.Extension))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(root, fileInfo.FullName).Replace(Path.DirectorySeparatorChar, '/');
            yield return new ModFile(relativePath, state, fileInfo.Length, fileInfo.LastWriteTimeUtc);
        }
    }
}
