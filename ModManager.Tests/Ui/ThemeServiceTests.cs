using ModManager.Ui.Models;
using ModManager.Ui.Services;

namespace ModManager.Tests.Ui;

[TestClass]
public sealed class ThemeServiceTests
{
    private string _themesDirectory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _themesDirectory = Path.Combine(Path.GetTempPath(), "ModManagerTests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_themesDirectory))
        {
            Directory.Delete(_themesDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ListThemes_WhenNoCustomThemesSaved_ThenReturnsOnlyBuiltins()
    {
        ThemeService service = new(_themesDirectory);

        List<string> names = [.. service.ListThemes().Select(t => t.Name)];

        CollectionAssert.AreEquivalent(ThemePresets.All.Select(t => t.Name).ToList(), names);
    }

    [TestMethod]
    public void Save_WhenThemeIsCustom_ThenListThemesIncludesIt()
    {
        ThemeService service = new(_themesDirectory);
        AppTheme custom = ThemePresets.DefaultLight with { Name = "My Theme" };

        service.Save(custom);

        Assert.IsTrue(service.ListThemes().Any(t => t.Name == "My Theme"));
    }

    [TestMethod]
    public void Save_WhenNameCollidesWithBuiltin_ThenThrows()
    {
        ThemeService service = new(_themesDirectory);
        AppTheme collision = ThemePresets.DefaultLight with { Name = ThemePresets.DefaultLight.Name };

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Save(collision));
    }

    [TestMethod]
    public void Delete_WhenThemeIsBuiltin_ThenThrows()
    {
        ThemeService service = new(_themesDirectory);

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Delete(ThemePresets.DefaultDark.Name));
    }

    [TestMethod]
    public void Delete_WhenThemeIsCustom_ThenListThemesNoLongerIncludesIt()
    {
        ThemeService service = new(_themesDirectory);
        AppTheme custom = ThemePresets.DefaultLight with { Name = "Temp" };
        service.Save(custom);

        service.Delete("Temp");

        Assert.IsFalse(service.ListThemes().Any(t => t.Name == "Temp"));
    }

    [TestMethod]
    public void Import_WhenFileIsMalformedJson_ThenReturnsNull()
    {
        ThemeService service = new(_themesDirectory);
        string badFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(badFile, "{ not valid json");

        try
        {
            AppTheme? result = service.Import(badFile);

            Assert.IsNull(result);
        }
        finally
        {
            File.Delete(badFile);
        }
    }

    [TestMethod]
    public void ExportThenImport_WhenThemeIsCustom_ThenRoundTripsTheSameTheme()
    {
        ThemeService service = new(_themesDirectory);
        AppTheme theme = ThemePresets.Plumbob with { Name = "Exported" };
        string exportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            service.Export(theme, exportPath);
            AppTheme? imported = service.Import(exportPath);

            Assert.AreEqual(theme, imported);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }
}
