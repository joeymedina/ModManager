using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsDiscoveryService
{
    private static readonly HashSet<string> SupportedExtensions = [".package", ".ts4script"];

    /// <summary>
    /// Discovers managed mods from active and disabled folders and merges stable IDs from manifest profile.
    /// </summary>
    public IReadOnlyList<ManagedMod> DiscoverMods(ModsFolderLayout layout, ManifestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(profile);

        Dictionary<string, ManifestMod> existingByPackageKey = profile.Mods
            .GroupBy(mod => mod.PackageKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<(string PackageKey, ManagedModFile File)> discoveredFiles =
        [
            .. EnumerateModFiles(layout.ModsFolderPath, ModFileState.Enabled),
            .. EnumerateModFiles(layout.DisabledModsFolderPath, ModFileState.Disabled)
        ];

        return [.. discoveredFiles
            .GroupBy(item => item.PackageKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                existingByPackageKey.TryGetValue(group.Key, out ManifestMod? existing);

                List<ManagedModFile> files = group
                    .Select(item => item.File)
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool isMixedState = files
                    .Select(file => file.State)
                    .Distinct()
                    .Skip(1)
                    .Any();

                string displayName = !string.IsNullOrWhiteSpace(existing?.Name)
                    ? existing.Name
                    : group.Key;

                return new ManagedMod(
                    existing?.ModId ?? Guid.NewGuid().ToString("N"),
                    displayName,
                    group.Key,
                    files,
                    isMixedState);
            })
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Maps a managed mod to its manifest representation.
    /// </summary>
    public ManifestMod ToManifestMod(ManagedMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return new ManifestMod
        {
            ModId = mod.ModId,
            Name = mod.Name,
            PackageKey = mod.PackageKey,
            Files = [.. mod.Files.Select(file => new ManifestModFile
            {
                RelativePath = file.RelativePath,
                State = file.State
            })]
        };
    }

    private static IEnumerable<(string PackageKey, ManagedModFile File)> EnumerateModFiles(string root, ModFileState state)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(root, filePath).Replace(Path.DirectorySeparatorChar, '/');
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath);
            string packageKey = DerivePackageKey(fileNameWithoutExtension);
            yield return (packageKey, new ManagedModFile(relativePath, state));
        }
    }

    // TODO: Consider using a more robust package key derivation strategy if needed.
    private static string DerivePackageKey(string fileNameWithoutExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameWithoutExtension);

        int separatorIndex = fileNameWithoutExtension.IndexOfAny(['_', '-']);
        string prefix = separatorIndex <= 0
            ? fileNameWithoutExtension
            : fileNameWithoutExtension[..separatorIndex];

        return prefix.Trim();
    }
}
