using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsFolderPathService
{
    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);

        string resolvedModsFolder = Path.GetFullPath(modsFolderPath);
        string normalizedModsFolder = resolvedModsFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string? parent = Path.GetDirectoryName(normalizedModsFolder);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new ArgumentException($"Could not resolve parent directory for '{modsFolderPath}'.", nameof(modsFolderPath));
        }

        string folderName = Path.GetFileName(normalizedModsFolder);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            throw new ArgumentException($"Could not resolve folder name for '{modsFolderPath}'.", nameof(modsFolderPath));
        }

        string disabledFolder = Path.Combine(parent, $"{folderName}.Disabled");
        return new ModsFolderLayout(resolvedModsFolder, disabledFolder);
    }

    /// <summary>
    /// Resolves a relative file path and validates it stays under the root.
    /// </summary>
    public string ResolveValidatedPath(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalizedRoot = Path.GetFullPath(root);
        string rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelativePath));

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid relative path '{relativePath}'.");
        }

        return fullPath;
    }

    /// <summary>
    /// Determines whether a record's files currently live under the disabled root rather than the
    /// enabled one, using the first file as a representative sample — a record's files move between
    /// <see cref="ModsFolderLayout.ModsFolderPath"/> and <see cref="ModsFolderLayout.DisabledModsFolderPath"/>
    /// together (enable/disable moves every file of a mod at once), so one sample is enough. Shared by
    /// every update/supersede path that needs to write back into wherever a mod actually lives instead
    /// of assuming it's enabled.
    /// </summary>
    public static string ResolveInstallRoot(ModsFolderLayout layout, InstallRecord? record)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (record is null || record.Files.Count == 0)
        {
            return layout.ModsFolderPath;
        }

        string sampleRelativePath = record.Files[0].RelativePath;
        bool livesInDisabledRoot = File.Exists(Path.Combine(layout.DisabledModsFolderPath, sampleRelativePath))
            && !File.Exists(Path.Combine(layout.ModsFolderPath, sampleRelativePath));

        return livesInDisabledRoot ? layout.DisabledModsFolderPath : layout.ModsFolderPath;
    }

    /// <summary>
    /// Sanitizes a desired folder name and, if it's already taken under any of the given roots,
    /// appends a numeric suffix until it's free in all of them. A fresh install checks just the one
    /// root it's writing into; a rename checks both the enabled and disabled root even though a
    /// record's files only ever live under one, so the chosen name can't collide with an unrelated
    /// mod sitting in the other.
    /// </summary>
    public static string ResolveDedupedFolderName(string desiredName, params string[] roots)
    {
        string sanitized = SanitizeFolderName(desiredName);

        string candidate = sanitized;
        for (int suffix = 2; roots.Any(root => Directory.Exists(Path.Combine(root, candidate))); suffix++)
        {
            candidate = $"{sanitized} ({suffix})";
        }

        return candidate;
    }

    /// <summary>
    /// Strips characters invalid in a folder name, falling back to "Mod" if nothing is left. Exposed
    /// so a caller can compare a desired name against an existing one (e.g. "is this rename actually
    /// a no-op?") using the same sanitization <see cref="ResolveDedupedFolderName"/> applies, without
    /// running the existence-check/dedup loop.
    /// </summary>
    public static string SanitizeFolderName(string desiredName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new([.. desiredName.Trim().Where(c => !invalidChars.Contains(c))]);
        return sanitized.Length == 0 ? "Mod" : sanitized;
    }
}
