using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ModManager.Ui.Models;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

/// <summary>
/// A discardable working copy of an <see cref="AppTheme"/>. Every edit is applied live via
/// <see cref="ThemeService"/> (so the dialog itself, styled by the same resources, previews the
/// change) — nothing here is persisted until the caller reads <see cref="ToTheme"/> and saves it.
/// </summary>
public partial class ThemeEditorViewModel : ViewModelBase
{
    private readonly ThemeService _themeService;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private Color _accent;

    [ObservableProperty]
    private Color _windowBackground;

    [ObservableProperty]
    private Color _cardBackground;

    [ObservableProperty]
    private Color _cardBorder;

    [ObservableProperty]
    private Color _controlFill;

    [ObservableProperty]
    private Color _textPrimary;

    [ObservableProperty]
    private Color _textSecondary;

    [ObservableProperty]
    private Color _success;

    [ObservableProperty]
    private Color _caution;

    [ObservableProperty]
    private Color _danger;

    [ObservableProperty]
    private string _fontFamily;

    // decimal to match NumericUpDown.Value's type exactly — binding a double there leaves a
    // stray DataValidationErrors adorner that visually collides with the spin buttons.
    [ObservableProperty]
    private decimal _fontSize;

    public string? ContrastWarning => BuildContrastWarning();

    public ThemeEditorViewModel()
        : this(new ThemeService(), ThemePresets.DefaultLight)
    {
    }

    public ThemeEditorViewModel(ThemeService themeService, AppTheme source)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(source);

        _themeService = themeService;
        _name = source.Name;
        _isDark = source.IsDark;
        _accent = Color.Parse(source.Accent);
        _windowBackground = Color.Parse(source.WindowBackground);
        _cardBackground = Color.Parse(source.CardBackground);
        _cardBorder = Color.Parse(source.CardBorder);
        _controlFill = Color.Parse(source.ControlFill);
        _textPrimary = Color.Parse(source.TextPrimary);
        _textSecondary = Color.Parse(source.TextSecondary);
        _success = Color.Parse(source.Success);
        _caution = Color.Parse(source.Caution);
        _danger = Color.Parse(source.Danger);
        _fontFamily = source.FontFamily;
        _fontSize = (decimal)source.FontSize;
    }

    public AppTheme ToTheme() => new()
    {
        Name = Name.Trim(),
        IsDark = IsDark,
        Accent = Accent.ToString(),
        WindowBackground = WindowBackground.ToString(),
        CardBackground = CardBackground.ToString(),
        CardBorder = CardBorder.ToString(),
        ControlFill = ControlFill.ToString(),
        TextPrimary = TextPrimary.ToString(),
        TextSecondary = TextSecondary.ToString(),
        Success = Success.ToString(),
        Caution = Caution.ToString(),
        Danger = Danger.ToString(),
        FontFamily = FontFamily,
        FontSize = (double)FontSize,
    };

    // Every edit (color, font, name — even IsDark) restyles the live app so the dialog previews
    // itself, and refreshes the contrast warning. Guarded on the property name to avoid the
    // ContrastWarning notification below re-entering this same override forever.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ContrastWarning))
        {
            return;
        }

        _themeService.Apply(ToTheme());
        OnPropertyChanged(nameof(ContrastWarning));
    }

    private string? BuildContrastWarning()
    {
        List<string> problems = [];
        if (ContrastRatio(TextPrimary, WindowBackground) < 4.5)
        {
            problems.Add("text on the window background");
        }

        if (ContrastRatio(TextPrimary, CardBackground) < 4.5)
        {
            problems.Add("text on cards");
        }

        return problems.Count == 0 ? null : $"Low contrast: {string.Join(", ", problems)} may be hard to read.";
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        double darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c) =>
        0.2126 * ToLinear(c.R / 255.0) + 0.7152 * ToLinear(c.G / 255.0) + 0.0722 * ToLinear(c.B / 255.0);

    private static double ToLinear(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
