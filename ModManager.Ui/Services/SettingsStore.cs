using System.Text.Json;

namespace ModManager.Ui.Services;

public sealed record AppSettings
{
    public string? ModsFolderPath { get; init; }
}

/// <summary>
/// Reads and writes the handful of user preferences that must survive a restart.
/// A corrupt or missing file is not worth failing startup over — it falls back to defaults.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;

    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManager",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that fails to persist is an annoyance, not a reason to lose the session.
        }
    }
}
