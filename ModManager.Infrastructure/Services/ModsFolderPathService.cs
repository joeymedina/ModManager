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
}
