using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services;

/// <summary>
/// Stores volatile per-record check state as JSON under <c>%LOCALAPPDATA%\ModManager\</c> — see
/// <see cref="UpdateCheckState"/> for why this is kept out of the Mods-folder manifest. Unlike
/// <see cref="ModsManifestService"/>, a corrupt or missing file is just re-derivable cache: there's
/// nothing to quarantine or protect, so a bad read yields an empty dictionary and the next check
/// simply repopulates it.
/// </summary>
public sealed class UpdateCheckStateStore(string? stateDirectory = null, ILogger<UpdateCheckStateStore>? logger = null) : IUpdateCheckStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<UpdateCheckStateStore> _logger = logger ?? NullLogger<UpdateCheckStateStore>.Instance;

    // Overridable so tests can point this at a sandbox instead of the real user profile — every other
    // on-disk service in this app takes its root as a parameter rather than hardcoding one.
    private readonly string _stateDirectory = stateDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");

    private string StatePath => Path.Combine(_stateDirectory, "update-check-state.json");

    public async Task<IReadOnlyDictionary<string, UpdateCheckState>> LoadAsync(CancellationToken cancellationToken = default)
    {
        string path = StatePath;
        if (!File.Exists(path))
        {
            return new Dictionary<string, UpdateCheckState>(StringComparer.Ordinal);
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            Dictionary<string, UpdateCheckState>? state = await JsonSerializer.DeserializeAsync<Dictionary<string, UpdateCheckState>>(stream, SerializerOptions, cancellationToken);
            return state ?? new Dictionary<string, UpdateCheckState>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Update check state at {StatePath} is not valid JSON; starting fresh", path);
            return new Dictionary<string, UpdateCheckState>(StringComparer.Ordinal);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read update check state at {StatePath}; starting fresh", path);
            return new Dictionary<string, UpdateCheckState>(StringComparer.Ordinal);
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, UpdateCheckState> state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        string path = StatePath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
    }
}
