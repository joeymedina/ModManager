using System.Text.Json;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsManifestService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads the per-folder manifest. Missing file, unreadable JSON, or an older schema all yield an
    /// empty manifest rather than throwing. Never writes.
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
            return manifest is null || manifest.SchemaVersion < ModsManifest.CurrentSchemaVersion
                ? ModsManifest.Empty
                : manifest;
        }
        catch (JsonException)
        {
            return ModsManifest.Empty;
        }
    }

    /// <summary>
    /// Saves the manifest into the mods folder, creating the folder if needed.
    /// </summary>
    public async Task SaveAsync(ModsFolderLayout layout, ModsManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(manifest);

        Directory.CreateDirectory(layout.ModsFolderPath);
        string path = GetManifestPath(layout);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);
    }

    private static string GetManifestPath(ModsFolderLayout layout) =>
        Path.Combine(layout.ModsFolderPath, ".modmanager.json");
}
