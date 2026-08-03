using System.Text.Json;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

public sealed class ModsManifestService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Loads manifest from AppData; creates an empty in-memory manifest when missing.
    /// </summary>
    public async Task<ManifestModel> LoadManifestAsync(CancellationToken cancellationToken)
    {
        string manifestPath = GetManifestPath();
        if (!File.Exists(manifestPath))
        {
            return new ManifestModel { Profiles = [] };
        }

        await using FileStream stream = File.OpenRead(manifestPath);
        ManifestModel? manifest = await JsonSerializer.DeserializeAsync<ManifestModel>(stream, SerializerOptions, cancellationToken);
        return manifest ?? new ManifestModel { Profiles = [] };
    }

    /// <summary>
    /// Saves manifest to AppData.
    /// </summary>
    public async Task SaveManifestAsync(ManifestModel manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string manifestPath = GetManifestPath();
        string? directory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Could not resolve manifest directory path.");
        }

        Directory.CreateDirectory(directory);

        await using FileStream stream = new(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Returns a profile for the mods folder path, creating one when needed.
    /// </summary>
    public ManifestProfile GetOrCreateProfile(ManifestModel manifest, string modsFolderPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);

        ManifestProfile? existing = manifest.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ModsFolderPath, modsFolderPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing;
        }

        ManifestProfile created = new()
        {
            ModsFolderPath = modsFolderPath,
            Mods = []
        };

        manifest.Profiles.Add(created);
        return created;
    }

    private static string GetManifestPath()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appDataPath))
        {
            throw new InvalidOperationException("Could not resolve AppData path for mod manifest storage.");
        }

        return Path.Combine(appDataPath, "ModManager", "mods-manifest.json");
    }
}
