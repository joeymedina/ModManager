namespace ModManager.Ui.Models;

/// <summary>
/// A user-facing color and typography palette. Colors are ARGB hex strings (e.g. "#FF6A5ACD") so a
/// theme round-trips through JSON with no custom converter and parses directly via
/// <see cref="Avalonia.Media.Color.Parse(string)"/>.
/// </summary>
public sealed record AppTheme
{
    public required string Name { get; init; }

    /// <summary>Which FluentAvalonia base variant this palette is built on.</summary>
    public required bool IsDark { get; init; }

    public required string Accent { get; init; }
    public required string WindowBackground { get; init; }
    public required string CardBackground { get; init; }
    public required string CardBorder { get; init; }
    public required string ControlFill { get; init; }
    public required string TextPrimary { get; init; }
    public required string TextSecondary { get; init; }
    public required string Success { get; init; }
    public required string Caution { get; init; }
    public required string Danger { get; init; }
    public required string FontFamily { get; init; }
    public required double FontSize { get; init; }
}
