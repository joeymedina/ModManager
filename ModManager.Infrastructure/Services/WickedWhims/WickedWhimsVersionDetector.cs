using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsVersionDetector
{
    private static readonly Regex[] TextPatterns =
    [
        new(@"WickedWhims[_\s-]+v?(\d{3}[a-z])", RegexOptions.IgnoreCase),
        new(@"WickedWhims[\s\S]{0,80}?(?:version|release|v)\s*[:=]?\s*(\d{3}[a-z])", RegexOptions.IgnoreCase),
        new(@"TURBODRIVER[\s\S]{0,80}?(?:version|release|v)\s*[:=]?\s*(\d{3}[a-z])", RegexOptions.IgnoreCase),
        new(@"(?:version|release)\s*(?:of\s*)?WickedWhims\s*[:=]?\s*(\d{3}[a-z])", RegexOptions.IgnoreCase)
    ];

    private static readonly Regex[] PythonPatterns =
    [
        new(@"__version__\s*=\s*[""'](\d{3}[a-z])[""']", RegexOptions.IgnoreCase),
        new(@"VERSION\s*=\s*[""'](\d{3}[a-z])[""']", RegexOptions.IgnoreCase)
    ];

    /// <summary>
    /// Finds the highest installed WickedWhims version in the supplied mods folder. When
    /// <paramref name="scopedRelativePaths"/> is non-empty, only those files (relative to
    /// <paramref name="folder"/>) are scanned instead of walking the whole tree — the full scan reads
    /// every matching file's bytes, which isn't viable on a large Mods folder. Pass a prior install
    /// record's file paths once one exists; full-tree scan is only for the no-record adoption case.
    /// </summary>
    public InstalledModVersion? FindInstalledVersion(string folder, IReadOnlyCollection<string>? scopedRelativePaths = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("A mods folder path is required.", nameof(folder));
        }

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Mods folder does not exist: {folder}");
        }

        IEnumerable<string> candidateFiles = scopedRelativePaths is { Count: > 0 }
            ? scopedRelativePaths.Select(path => Path.Combine(folder, path)).Where(File.Exists)
            : Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);

        IEnumerable<InstalledModVersion> candidates = candidateFiles
            .Where(file => Regex.IsMatch(file, @"\.(ts4script|package|py)$", RegexOptions.IgnoreCase))
            .Select(file => (File: file, Version: ExtractVersion(file)))
            .Where(item => item.Version is not null)
            .Select(item => new InstalledModVersion(item.Version!, Path.GetRelativePath(folder, item.File)))
            .OrderByDescending(item => item.Version, Comparer<string>.Create((left, right) => CompareVersions(left, right) ?? 0));

        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Compares WickedWhims-style version values like 187a and 187b.
    /// </summary>
    public static int? CompareVersions(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        Match leftMatch = Regex.Match(left.Trim(), @"^(\d+)([a-z]*)$", RegexOptions.IgnoreCase);
        Match rightMatch = Regex.Match(right.Trim(), @"^(\d+)([a-z]*)$", RegexOptions.IgnoreCase);

        if (!leftMatch.Success || !rightMatch.Success)
        {
            return null;
        }

        int numberComparison = int.Parse(leftMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            .CompareTo(int.Parse(rightMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        return numberComparison != 0
            ? Math.Sign(numberComparison)
            : string.Compare(leftMatch.Groups[2].Value, rightMatch.Groups[2].Value, StringComparison.OrdinalIgnoreCase) switch
            {
                0 => 0,
                > 0 => 1,
                _ => -1
            };
    }

    private static string? ExtractVersion(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);

        if (Path.GetExtension(file).Equals(".ts4script", StringComparison.OrdinalIgnoreCase))
        {
            string? archiveVersion = ExtractVersionFromScript(bytes);
            if (archiveVersion is not null)
            {
                return archiveVersion;
            }
        }

        return ExtractVersionFromText(Encoding.UTF8.GetString(bytes), TextPatterns);
    }

    private static string? ExtractVersionFromScript(byte[] bytes)
    {
        try
        {
            using ZipArchive archive = new(new MemoryStream(bytes), ZipArchiveMode.Read);
            IEnumerable<ZipArchiveEntry> entries = archive.Entries
                .Where(entry => entry.FullName.EndsWith("/__init__.py", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Equals("__init__.py", StringComparison.OrdinalIgnoreCase))
                .Concat(archive.Entries);

            foreach (ZipArchiveEntry entry in entries)
            {
                using StreamReader reader = new(entry.Open());
                string? version = ExtractVersionFromText(reader.ReadToEnd(), PythonPatterns.Concat(TextPatterns));
                if (version is not null)
                {
                    return version;
                }
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractVersionFromText(string text, IEnumerable<Regex> patterns)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(patterns);

        return patterns
            .SelectMany(pattern => pattern.Matches(text).Cast<Match>())
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .OrderByDescending(version => int.Parse(version[..^1], System.Globalization.CultureInfo.InvariantCulture))
            .ThenByDescending(version => version, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

internal sealed record InstalledModVersion(string Version, string Source);
