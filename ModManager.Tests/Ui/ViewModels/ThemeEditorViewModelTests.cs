using Avalonia.Media;
using ModManager.Ui.Models;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ThemeEditorViewModelTests
{
    private string themesDirectory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        themesDirectory = Path.Combine(Path.GetTempPath(), "ModManagerTests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(themesDirectory))
        {
            Directory.Delete(themesDirectory, recursive: true);
        }
    }

    private ThemeEditorViewModel CreateEditor(AppTheme source) => new(new ThemeService(themesDirectory), source);

    [TestMethod]
    public void Constructor_WhenGivenASource_ThenSeedsEveryFieldFromIt()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.Plumbob);

        Assert.AreEqual(ThemePresets.Plumbob.Name, editor.Name);
        Assert.AreEqual(ThemePresets.Plumbob.IsDark, editor.IsDark);
        Assert.AreEqual(Color.Parse(ThemePresets.Plumbob.Accent), editor.Accent);
        Assert.AreEqual(ThemePresets.Plumbob.FontFamily, editor.FontFamily);
        Assert.AreEqual((decimal)ThemePresets.Plumbob.FontSize, editor.FontSize);
    }

    [TestMethod]
    public void ToTheme_WhenNoEditsMade_ThenRoundTripsTheSameColors()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultDark);

        AppTheme result = editor.ToTheme();

        Assert.AreEqual(Color.Parse(ThemePresets.DefaultDark.Accent), Color.Parse(result.Accent));
        Assert.AreEqual(Color.Parse(ThemePresets.DefaultDark.WindowBackground), Color.Parse(result.WindowBackground));
        Assert.AreEqual(Color.Parse(ThemePresets.DefaultDark.Danger), Color.Parse(result.Danger));
        Assert.AreEqual(ThemePresets.DefaultDark.IsDark, result.IsDark);
    }

    [TestMethod]
    public void ToTheme_WhenNameHasSurroundingWhitespace_ThenTrimsIt()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultLight);

        editor.Name = "  My Theme  ";

        Assert.AreEqual("My Theme", editor.ToTheme().Name);
    }

    [TestMethod]
    public void ToTheme_WhenAColorIsChanged_ThenReflectsTheEdit()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultLight);

        editor.Accent = Colors.Red;

        Assert.AreEqual(Colors.Red, Color.Parse(editor.ToTheme().Accent));
    }

    [TestMethod]
    public void ContrastWarning_WhenTextMatchesBackground_ThenReturnsAWarning()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultLight);

        editor.TextPrimary = Colors.White;
        editor.WindowBackground = Colors.White;

        Assert.IsNotNull(editor.ContrastWarning);
    }

    [TestMethod]
    public void ContrastWarning_WhenTextAndBackgroundContrastWell_ThenReturnsNull()
    {
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultLight);

        editor.TextPrimary = Colors.Black;
        editor.WindowBackground = Colors.White;
        editor.CardBackground = Colors.White;

        Assert.IsNull(editor.ContrastWarning);
    }

    [TestMethod]
    public void ColorProperty_WhenChanged_ThenDoesNotRecurseOnContrastWarningNotification()
    {
        // OnPropertyChanged re-raises ContrastWarning itself; a missing guard against that
        // self-notification would stack-overflow every time a color changes.
        ThemeEditorViewModel editor = CreateEditor(ThemePresets.DefaultLight);

        editor.Accent = Colors.Blue;

        Assert.AreEqual(Colors.Blue, editor.Accent);
    }
}
