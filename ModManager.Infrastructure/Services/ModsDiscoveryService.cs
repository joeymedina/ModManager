using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsDiscoveryService(ILogger<ModsDiscoveryService>? logger = null)
{
    private static readonly HashSet<string> SupportedExtensions = [".package", ".ts4script"];

    private readonly ILogger<ModsDiscoveryService> _logger = logger ?? NullLogger<ModsDiscoveryService>.Instance;

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

        int enabledCount = byPath.Count;
        int conflictedCount = 0;

        foreach (ModFile file in EnumerateModFiles(layout.DisabledModsFolderPath, ModFileState.Disabled))
        {
            if (byPath.TryGetValue(file.RelativePath, out ModFile? existing))
            {
                byPath[file.RelativePath] = existing with { IsConflicted = true };
                conflictedCount++;
            }
            else
            {
                byPath[file.RelativePath] = file;
            }
        }

        _logger.LogDebug(
            "Discovery: {EnabledCount} enabled in {ModsFolder}, {DisabledCount} disabled in {DisabledFolder}, {ConflictedCount} present in both",
            enabledCount,
            layout.ModsFolderPath,
            byPath.Count - enabledCount,
            layout.DisabledModsFolderPath,
            conflictedCount);

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
