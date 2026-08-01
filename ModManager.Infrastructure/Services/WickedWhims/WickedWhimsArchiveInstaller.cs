using System.IO.Compression;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsArchiveInstaller
{
    /// <summary>
    /// Installs a WickedWhims archive into the target mods folder.
    /// </summary>
    public int InstallArchive(string folder, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(bytes);

        Directory.CreateDirectory(folder);
        using ZipArchive archive = new(new MemoryStream(bytes), ZipArchiveMode.Read);

        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        int written = 0;

        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsafe archive path: {entry.FullName}");
            }

            string? directory = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"Could not resolve directory for archive entry '{entry.FullName}'.");
            }

            Directory.CreateDirectory(directory);
            entry.ExtractToFile(target, overwrite: true);
            written++;
        }

        return written;
    }
}
