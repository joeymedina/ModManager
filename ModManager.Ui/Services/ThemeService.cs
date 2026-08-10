using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.Styling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Ui.Models;

namespace ModManager.Ui.Services;

/// <summary>
/// Applies an <see cref="AppTheme"/> and persists custom ones as JSON under
/// %APPDATA%\ModManager\themes. Applying writes flat (variant-unscoped) entries into
/// <c>Application.Resources</c> for both the Color and Brush key of each slot — a flat key there wins
/// over FluentAvaloniaTheme's variant-scoped default regardless of RequestedThemeVariant (confirmed
/// live), which is what lets one palette replace the light/dark toggle instead of following it.
/// </summary>
public sealed class ThemeService(string? themesDirectory = null, ILogger<ThemeService>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _themesDirectory = themesDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManager", "themes");

    private readonly ILogger<ThemeService> _logger = logger ?? NullLogger<ThemeService>.Instance;
    private readonly ResourceDictionary _overrides = new();
    private bool _installed;

    public IReadOnlyList<AppTheme> ListThemes()
    {
        List<AppTheme> themes = [.. ThemePresets.All];

        if (Directory.Exists(_themesDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_themesDirectory, "*.json"))
            {
                if (TryLoad(file, out AppTheme? theme))
                {
                    themes.Add(theme);
                }
            }
        }

        return themes;
    }

    public void Apply(AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (Avalonia.Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = theme.IsDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;

        if (app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } fluentTheme)
        {
            fluentTheme.CustomAccentColor = Color.Parse(theme.Accent);
        }

        WriteColor("SolidBackgroundFillColorBase", theme.WindowBackground);
        WriteColor("CardBackgroundFillColorDefault", theme.CardBackground);
        WriteColor("CardStrokeColorDefault", theme.CardBorder);
        WriteColor("ControlFillColorDefault", theme.ControlFill);
        WriteColor("TextFillColorPrimary", theme.TextPrimary);
        WriteColor("TextFillColorSecondary", theme.TextSecondary);
        WriteColor("SystemFillColorSuccess", theme.Success);
        WriteColor("SystemFillColorCaution", theme.Caution);
        WriteColor("SystemFillColorCritical", theme.Danger);
        _overrides["ContentControlThemeFontFamily"] = new FontFamily(theme.FontFamily);
        _overrides["ControlContentThemeFontSize"] = theme.FontSize;

        if (!_installed)
        {
            app.Resources.MergedDictionaries.Add(_overrides);
            _installed = true;
        }
    }

    public void Save(AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ThrowIfBuiltin(theme.Name, "saved over");

        try
        {
            Directory.CreateDirectory(_themesDirectory);
            File.WriteAllText(PathFor(theme.Name), JsonSerializer.Serialize(theme, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to save theme {ThemeName}", theme.Name);
        }
    }

    public void Delete(string name)
    {
        ThrowIfBuiltin(name, "deleted");

        try
        {
            File.Delete(PathFor(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete theme {ThemeName}", name);
        }
    }

    public AppTheme? Import(string filePath) => TryLoad(filePath, out AppTheme? theme) ? theme : null;

    public void Export(AppTheme theme, string filePath)
    {
        ArgumentNullException.ThrowIfNull(theme);
        File.WriteAllText(filePath, JsonSerializer.Serialize(theme, Options));
    }

    private static void ThrowIfBuiltin(string name, string action)
    {
        if (ThemePresets.All.Any(preset => preset.Name == name))
        {
            throw new InvalidOperationException($"\"{name}\" is a built-in theme and can't be {action}.");
        }
    }

    private bool TryLoad(string filePath, [NotNullWhen(true)] out AppTheme? theme)
    {
        try
        {
            theme = JsonSerializer.Deserialize<AppTheme>(File.ReadAllText(filePath));
            return theme is not null && !string.IsNullOrWhiteSpace(theme.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to load theme from {FilePath}", filePath);
            theme = null;
            return false;
        }
    }

    private void WriteColor(string key, string hex)
    {
        Color color = Color.Parse(hex);
        _overrides[key] = color;
        _overrides[$"{key}Brush"] = new SolidColorBrush(color);
    }

    private string PathFor(string themeName) => Path.Combine(_themesDirectory, $"{SanitizeFileName(themeName)}.json");

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string([.. name.Where(c => !invalid.Contains(c))]).Trim();
        return string.IsNullOrEmpty(cleaned) ? "theme" : cleaned;
    }
}
