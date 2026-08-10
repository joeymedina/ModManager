using ModManager.Ui.Models;

namespace ModManager.Ui.Services;

/// <summary>
/// Built-in themes. Not files on disk — always available, never overwritable or deletable, and the
/// starting point a user duplicates to make their own. Light/dark values are FluentAvalonia's actual
/// defaults (captured live), so "Default Light/Dark" match the app's out-of-box look exactly.
/// </summary>
public static class ThemePresets
{
    public static readonly AppTheme DefaultLight = new()
    {
        Name = "Default Light",
        IsDark = false,
        Accent = "#FF6A5ACD",
        WindowBackground = "#FFF3F3F3",
        CardBackground = "#B3FFFFFF",
        CardBorder = "#0F000000",
        ControlFill = "#B3FFFFFF",
        TextPrimary = "#E4000000",
        TextSecondary = "#9E000000",
        Success = "#FF0F7B0F",
        Caution = "#FF9D5D00",
        Danger = "#FFC42B1C",
        FontFamily = "Segoe UI Variable",
        FontSize = 14,
    };

    public static readonly AppTheme DefaultDark = new()
    {
        Name = "Default Dark",
        IsDark = true,
        Accent = "#FF6A5ACD",
        WindowBackground = "#FF202020",
        CardBackground = "#0DFFFFFF",
        CardBorder = "#19000000",
        ControlFill = "#0FFFFFFF",
        TextPrimary = "#FFFFFFFF",
        TextSecondary = "#C5FFFFFF",
        Success = "#FF6CCB5F",
        Caution = "#FFFCE100",
        Danger = "#FFFF99A4",
        FontFamily = "Segoe UI Variable",
        FontSize = 14,
    };

    public static readonly AppTheme Plumbob = new()
    {
        Name = "Plumbob",
        IsDark = true,
        Accent = "#FF1EB980",
        WindowBackground = "#FF14231C",
        CardBackground = "#1A1EB980",
        CardBorder = "#331EB980",
        ControlFill = "#141EB980",
        TextPrimary = "#FFE8FFF3",
        TextSecondary = "#B3E8FFF3",
        Success = "#FF34D399",
        Caution = "#FFFCE100",
        Danger = "#FFFF6B6B",
        FontFamily = "Segoe UI Variable",
        FontSize = 14,
    };

    public static readonly IReadOnlyList<AppTheme> All = [DefaultLight, DefaultDark, Plumbob];
}
