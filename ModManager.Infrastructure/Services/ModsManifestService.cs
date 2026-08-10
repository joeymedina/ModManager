using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsManifestService(ILogger<ModsManifestService>? logger = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<ModsManifestService> _logger = logger ?? NullLogger<ModsManifestService>.Instance;

    /// <summary>
    /// Loads the per-folder manifest. A missing file yields an empty manifest. Unreadable JSON or an
    /// older schema also yield an empty manifest, but one flagged <see cref="ModsManifest.UnreadableReason"/>
    /// and backed up on disk first, so <see cref="SaveAsync"/> can refuse to overwrite the original.
    /// Never throws.
    /// </summary>
    public async Task<ModsManifest> LoadAsync(ModsFolderLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        string path = GetManifestPath(layout);
        if (!File.Exists(path))
        {
            return ModsManifest.Empty;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            ModsManifest? manifest = await JsonSerializer.DeserializeAsync<ModsManifest>(stream, SerializerOptions, cancellationToken);

            if (manifest is null)
            {
                return Quarantine(path, "the file contained no manifest data", null);
            }

            if (manifest.SchemaVersion < ModsManifest.CurrentSchemaVersion)
            {
                return Quarantine(
                    path,
                    $"it uses schema version {manifest.SchemaVersion}, older than the supported version {ModsManifest.CurrentSchemaVersion}",
                    null);
            }

            _logger.LogDebug(
                "Loaded manifest from {ManifestPath}: {FileCount} file entries, {GroupCount} groups, {InstallCount} installs",
                path,
                manifest.Files.Count,
                manifest.Groups.Count,
                manifest.Installs.Count);

            return manifest;
        }
        catch (JsonException ex)
        {
            return Quarantine(path, "the file is not valid JSON", ex);
        }
        catch (IOException ex)
        {
            // Couldn't even read it — don't claim it's corrupt, but still refuse to overwrite it.
            _logger.LogError(ex, "Could not read manifest at {ManifestPath}", path);
            return ModsManifest.Empty with { UnreadableReason = $"the file could not be read ({ex.Message})" };
        }
    }

    /// <summary>
    /// Saves the manifest into the mods folder, creating the folder if needed. Refuses to write when
    /// the manifest is the empty stand-in for one that failed to load, since that would replace the
    /// user's real install history with nothing.
    /// </summary>
    public async Task SaveAsync(ModsFolderLayout layout, ModsManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.UnreadableReason is { } reason)
        {
            // Single guard for every write path (install, adopt, group, WickedWhims update) — they all
            // load-then-save, so blocking here is what stops a bad load from erasing the manifest.
            _logger.LogError("Refused to save manifest for {ModsFolder}: {Reason}", layout.ModsFolderPath, reason);
            throw new InvalidOperationException(
                $"The existing mods manifest could not be read ({reason}). It has been backed up next to it, "
                + "and this change was not saved so the backup isn't overwritten. Restore or delete the manifest, then try again.");
        }

        Directory.CreateDirectory(layout.ModsFolderPath);
        string path = GetManifestPath(layout);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);

        _logger.LogInformation(
            "Saved manifest to {ManifestPath}: {FileCount} file entries, {GroupCount} groups, {InstallCount} installs",
            path,
            manifest.Files.Count,
            manifest.Groups.Count,
            manifest.Installs.Count);
    }

    /// <summary>
    /// Copies an unusable manifest aside so the data survives, and returns the flagged empty stand-in.
    /// </summary>
    private ModsManifest Quarantine(string path, string reason, Exception? exception)
    {
        string backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(path, backupPath, overwrite: true);
            _logger.LogError(
                exception,
                "Manifest at {ManifestPath} is unusable because {Reason}; backed it up to {BackupPath}. "
                + "Install history, display names, and groups are unavailable until it is restored or removed",
                path,
                reason,
                backupPath);
        }
        catch (IOException copyFailure)
        {
            _logger.LogError(
                copyFailure,
                "Manifest at {ManifestPath} is unusable because {Reason}, and the backup copy to {BackupPath} also failed",
                path,
                reason,
                backupPath);
        }

        return ModsManifest.Empty with { UnreadableReason = reason };
    }

    private static string GetManifestPath(ModsFolderLayout layout) =>
        Path.Combine(layout.ModsFolderPath, ".modmanager.json");
}
