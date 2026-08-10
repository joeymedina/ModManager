using Moq;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Ui.Models;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class SettingsPageViewModelTests
{
    private Mock<IModsFolderUseCase> modsFolderUseCaseMock = null!;
    private Mock<IArchiveInstallService> archiveInstallServiceMock = null!;
    private Mock<IDialogService> modsDialogServiceMock = null!;
    private Mock<IDialogService> settingsDialogServiceMock = null!;
    private string sandboxPath = string.Empty;
    private SettingsStore lastSettingsStore = null!;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandboxPath);

        modsFolderUseCaseMock = new Mock<IModsFolderUseCase>();
        archiveInstallServiceMock = new Mock<IArchiveInstallService>();
        modsDialogServiceMock = new Mock<IDialogService>();
        settingsDialogServiceMock = new Mock<IDialogService>();

        modsFolderUseCaseMock
            .Setup(useCase => useCase.GetLayout(It.IsAny<string>()))
            .Returns((string path) => new ModsFolderLayout(path, $"{path}.Disabled"));
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModGroup>)[]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!string.IsNullOrWhiteSpace(sandboxPath) && Directory.Exists(sandboxPath))
        {
            Directory.Delete(sandboxPath, recursive: true);
        }
    }

    private ModsPageViewModel CreateModsViewModel(string modsFolderPath)
    {
        string settingsFilePath = Path.Combine(sandboxPath, $"{Guid.NewGuid():N}.json");
        SettingsStore settings = new(settingsFilePath);
        settings.Save(new AppSettings { ModsFolderPath = modsFolderPath });
        lastSettingsStore = settings;

        return new ModsPageViewModel(
            modsFolderUseCaseMock.Object,
            archiveInstallServiceMock.Object,
            modsDialogServiceMock.Object,
            settings);
    }

    // Shares the SettingsStore created by CreateModsViewModel, matching the DI singleton both
    // view models see in the real app — a theme write from one must be visible to the other.
    private SettingsPageViewModel CreateSettingsViewModel(ModsPageViewModel mods) =>
        new(mods, settingsDialogServiceMock.Object, new ThemeService(Path.Combine(sandboxPath, "themes")), lastSettingsStore);

    // --- Constructor --------------------------------------------------------

    [TestMethod]
    public void Constructor_WhenCalled_ThenSeedsModsFolderPathFromWrappedModsPageViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");

        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);

        Assert.AreEqual("C:/Mods", settingsViewModel.ModsFolderPath);
    }

    // --- DisabledModsFolderPath ----------------------------------------------

    [TestMethod]
    public void DisabledModsFolderPath_WhenRead_ThenForwardsFromWrappedModsPageViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);

        Assert.AreEqual(mods.DisabledModsFolderPath, settingsViewModel.DisabledModsFolderPath);
    }

    [TestMethod]
    public async Task DisabledModsFolderPath_WhenModsViewModelChangesFolder_ThenRaisesPropertyChangedOnSettingsViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        List<string?> raisedProperties = [];
        settingsViewModel.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await mods.SetModsFolderAsync("D:/NewMods");

        Assert.Contains(nameof(SettingsPageViewModel.DisabledModsFolderPath), raisedProperties);
        Assert.AreEqual("D:/NewMods.Disabled", settingsViewModel.DisabledModsFolderPath);
    }

    // --- ApplyAsync -----------------------------------------------------------

    [TestMethod]
    public async Task ApplyAsync_WhenModsFolderPathIsWhitespace_ThenSetsStatusMessageAndDoesNotCallTheUseCase()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.ModsFolderPath = "   ";

        await settingsViewModel.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual("Enter a folder path first.", settingsViewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ApplyAsync_WhenModsFolderPathIsValid_ThenTrimsAndForwardsToTheWrappedModsPageViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.ModsFolderPath = "  D:/NewMods  ";

        await settingsViewModel.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual("D:/NewMods", mods.ModsFolderPath);
        Assert.AreEqual("D:/NewMods", settingsViewModel.ModsFolderPath);
        Assert.AreEqual("Saved. The Mods page has been reloaded.", settingsViewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync("D:/NewMods", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- BrowseAsync ------------------------------------------------------------

    [TestMethod]
    public async Task BrowseAsync_WhenAPathIsPicked_ThenUpdatesModsFolderPathAndApplies()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsDialogServiceMock
            .Setup(dialog => dialog.PickFolderAsync("Choose your Mods folder", "C:/Mods"))
            .ReturnsAsync("E:/PickedMods");

        await settingsViewModel.BrowseCommand.ExecuteAsync(null);

        Assert.AreEqual("E:/PickedMods", settingsViewModel.ModsFolderPath);
        Assert.AreEqual("E:/PickedMods", mods.ModsFolderPath);
        Assert.AreEqual("Saved. The Mods page has been reloaded.", settingsViewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync("E:/PickedMods", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task BrowseAsync_WhenPickerIsCanceled_ThenLeavesModsFolderPathUnchangedAndDoesNotApply()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsDialogServiceMock
            .Setup(dialog => dialog.PickFolderAsync("Choose your Mods folder", "C:/Mods"))
            .ReturnsAsync((string?)null);

        await settingsViewModel.BrowseCommand.ExecuteAsync(null);

        Assert.AreEqual("C:/Mods", settingsViewModel.ModsFolderPath);
        Assert.AreEqual(string.Empty, settingsViewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Theme selection --------------------------------------------------------

    [TestMethod]
    public void Constructor_WhenCalled_ThenSeedsSelectedThemeFromSettings()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        lastSettingsStore.Save(lastSettingsStore.Load() with { ThemeName = ThemePresets.Plumbob.Name });

        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);

        Assert.AreEqual(ThemePresets.Plumbob.Name, settingsViewModel.SelectedTheme.Name);
    }

    [TestMethod]
    public void SelectedTheme_WhenChanged_ThenPersistsThemeNameWithoutLosingModsFolderPath()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);

        settingsViewModel.SelectedTheme = ThemePresets.DefaultDark;

        AppSettings saved = lastSettingsStore.Load();
        Assert.AreEqual(ThemePresets.DefaultDark.Name, saved.ThemeName);
        Assert.AreEqual("C:/Mods", saved.ModsFolderPath);
    }

    [TestMethod]
    public void SelectedTheme_WhenBindingAssignsNull_ThenDoesNotThrow()
    {
        // Avalonia's ComboBox nulls SelectedItem when the bound item is removed from ItemsSource
        // while selected, and that round-trips through the two-way binding into this setter.
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);

        settingsViewModel.SelectedTheme = null!;
    }

    // --- DuplicateTheme --------------------------------------------------------

    [TestMethod]
    public void DuplicateTheme_WhenCalled_ThenAddsACopyAndSelectsIt()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;

        settingsViewModel.DuplicateThemeCommand.Execute(null);

        Assert.AreEqual("Copy of Default Light", settingsViewModel.SelectedTheme.Name);
        Assert.IsTrue(settingsViewModel.AvailableThemes.Any(theme => theme.Name == "Copy of Default Light"));
    }

    [TestMethod]
    public void DuplicateTheme_WhenCopyNameAlreadyExists_ThenAppendsANumberedSuffix()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        settingsViewModel.DuplicateThemeCommand.Execute(null);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;

        settingsViewModel.DuplicateThemeCommand.Execute(null);

        Assert.AreEqual("Copy of Default Light (2)", settingsViewModel.SelectedTheme.Name);
    }

    // --- DeleteThemeAsync --------------------------------------------------------

    [TestMethod]
    public void DeleteThemeCommand_WhenSelectedThemeIsBuiltin_ThenCanExecuteIsFalse()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;

        Assert.IsFalse(settingsViewModel.DeleteThemeCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task DeleteThemeAsync_WhenConfirmed_ThenRemovesTheThemeAndSelectsDefaultLight()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        settingsViewModel.DuplicateThemeCommand.Execute(null);
        settingsDialogServiceMock
            .Setup(dialog => dialog.ConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ReturnsAsync(true);

        await settingsViewModel.DeleteThemeCommand.ExecuteAsync(null);

        Assert.IsFalse(settingsViewModel.AvailableThemes.Any(theme => theme.Name == "Copy of Default Light"));
        Assert.AreEqual(ThemePresets.DefaultLight.Name, settingsViewModel.SelectedTheme.Name);
    }

    [TestMethod]
    public async Task DeleteThemeAsync_WhenNotConfirmed_ThenKeepsTheTheme()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        settingsViewModel.DuplicateThemeCommand.Execute(null);
        settingsDialogServiceMock
            .Setup(dialog => dialog.ConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ReturnsAsync(false);

        await settingsViewModel.DeleteThemeCommand.ExecuteAsync(null);

        Assert.IsTrue(settingsViewModel.AvailableThemes.Any(theme => theme.Name == "Copy of Default Light"));
    }

    // --- ImportThemeAsync --------------------------------------------------------

    [TestMethod]
    public async Task ImportThemeAsync_WhenFileIsInvalid_ThenSetsStatusMessageAndDoesNotChangeSelection()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        AppTheme before = settingsViewModel.SelectedTheme;
        string badFile = Path.Combine(sandboxPath, "bad-theme.json");
        File.WriteAllText(badFile, "{ not valid json");
        settingsDialogServiceMock
            .Setup(dialog => dialog.PickFileAsync("Import theme", It.IsAny<IReadOnlyList<string>>(), "Theme files"))
            .ReturnsAsync(badFile);

        await settingsViewModel.ImportThemeCommand.ExecuteAsync(null);

        Assert.AreEqual("That file isn't a valid theme.", settingsViewModel.StatusMessage);
        Assert.AreEqual(before.Name, settingsViewModel.SelectedTheme.Name);
    }

    [TestMethod]
    public async Task ImportThemeAsync_WhenFileIsAValidTheme_ThenAddsAndSelectsIt()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        AppTheme imported = ThemePresets.DefaultLight with { Name = "Imported Theme" };
        string themeFile = Path.Combine(sandboxPath, "theme.json");
        File.WriteAllText(themeFile, System.Text.Json.JsonSerializer.Serialize(imported));
        settingsDialogServiceMock
            .Setup(dialog => dialog.PickFileAsync("Import theme", It.IsAny<IReadOnlyList<string>>(), "Theme files"))
            .ReturnsAsync(themeFile);

        await settingsViewModel.ImportThemeCommand.ExecuteAsync(null);

        Assert.AreEqual("Imported Theme", settingsViewModel.SelectedTheme.Name);
        Assert.IsTrue(settingsViewModel.AvailableThemes.Any(theme => theme.Name == "Imported Theme"));
    }

    [TestMethod]
    public async Task ImportThemeAsync_WhenNameCollidesWithABuiltin_ThenRenamesBeforeAdding()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        AppTheme imported = ThemePresets.DefaultLight with { Name = ThemePresets.DefaultLight.Name };
        string themeFile = Path.Combine(sandboxPath, "theme.json");
        File.WriteAllText(themeFile, System.Text.Json.JsonSerializer.Serialize(imported));
        settingsDialogServiceMock
            .Setup(dialog => dialog.PickFileAsync("Import theme", It.IsAny<IReadOnlyList<string>>(), "Theme files"))
            .ReturnsAsync(themeFile);

        await settingsViewModel.ImportThemeCommand.ExecuteAsync(null);

        Assert.AreEqual("Default Light (imported)", settingsViewModel.SelectedTheme.Name);
    }

    // --- ExportThemeAsync --------------------------------------------------------

    [TestMethod]
    public async Task ExportThemeAsync_WhenPathIsPicked_ThenWritesTheThemeToDisk()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.Plumbob;
        string exportPath = Path.Combine(sandboxPath, "exported.json");
        settingsDialogServiceMock
            .Setup(dialog => dialog.SaveFileAsync("Export theme", "Plumbob.json", ".json", "Theme files"))
            .ReturnsAsync(exportPath);

        await settingsViewModel.ExportThemeCommand.ExecuteAsync(null);

        Assert.IsTrue(File.Exists(exportPath));
        Assert.AreEqual($"Exported to {exportPath}.", settingsViewModel.StatusMessage);
    }

    [TestMethod]
    public async Task ExportThemeAsync_WhenPickerIsCanceled_ThenDoesNotWriteAFile()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsDialogServiceMock
            .Setup(dialog => dialog.SaveFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        await settingsViewModel.ExportThemeCommand.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, settingsViewModel.StatusMessage);
    }

    // --- ResetTheme --------------------------------------------------------

    [TestMethod]
    public void ResetTheme_WhenCalled_ThenSelectsDefaultLight()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.Plumbob;

        settingsViewModel.ResetThemeCommand.Execute(null);

        Assert.AreEqual(ThemePresets.DefaultLight.Name, settingsViewModel.SelectedTheme.Name);
    }

    // --- EditThemeAsync --------------------------------------------------------

    [TestMethod]
    public async Task EditThemeAsync_WhenSelectedThemeIsBuiltin_ThenSeedsTheEditorWithACopyName()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        string? seededName = null;
        settingsDialogServiceMock
            .Setup(dialog => dialog.ShowAsync("Edit theme", AppDialog.ThemeEditor, It.IsAny<object>(), "Save"))
            .Callback<string, AppDialog, object, string>((_, _, context, _) => seededName = ((ThemeEditorViewModel)context).Name)
            .ReturnsAsync(false);

        await settingsViewModel.EditThemeCommand.ExecuteAsync(null);

        Assert.AreEqual("Copy of Default Light", seededName);
    }

    [TestMethod]
    public async Task EditThemeAsync_WhenConfirmed_ThenSavesUnderTheEditedNameAndSelectsIt()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        settingsDialogServiceMock
            .Setup(dialog => dialog.ShowAsync("Edit theme", AppDialog.ThemeEditor, It.IsAny<object>(), "Save"))
            .Callback<string, AppDialog, object, string>((_, _, context, _) => ((ThemeEditorViewModel)context).Name = "My Custom Theme")
            .ReturnsAsync(true);

        await settingsViewModel.EditThemeCommand.ExecuteAsync(null);

        Assert.AreEqual("My Custom Theme", settingsViewModel.SelectedTheme.Name);
        Assert.IsTrue(settingsViewModel.AvailableThemes.Any(theme => theme.Name == "My Custom Theme"));
    }

    [TestMethod]
    public async Task EditThemeAsync_WhenCanceled_ThenLeavesSelectionAndAvailableThemesUnchanged()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        int themeCountBefore = settingsViewModel.AvailableThemes.Count;
        settingsDialogServiceMock
            .Setup(dialog => dialog.ShowAsync("Edit theme", AppDialog.ThemeEditor, It.IsAny<object>(), "Save"))
            .Callback<string, AppDialog, object, string>((_, _, context, _) => ((ThemeEditorViewModel)context).Name = "Never Saved")
            .ReturnsAsync(false);

        await settingsViewModel.EditThemeCommand.ExecuteAsync(null);

        Assert.AreEqual(ThemePresets.DefaultLight.Name, settingsViewModel.SelectedTheme.Name);
        Assert.HasCount(themeCountBefore, settingsViewModel.AvailableThemes);
    }

    [TestMethod]
    public async Task EditThemeAsync_WhenSavedNameIsRenamedBackToABuiltin_ThenSetsStatusMessageAndDoesNotChangeSelection()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = CreateSettingsViewModel(mods);
        settingsViewModel.SelectedTheme = ThemePresets.DefaultLight;
        settingsDialogServiceMock
            .Setup(dialog => dialog.ShowAsync("Edit theme", AppDialog.ThemeEditor, It.IsAny<object>(), "Save"))
            .Callback<string, AppDialog, object, string>(
                (_, _, context, _) => ((ThemeEditorViewModel)context).Name = ThemePresets.DefaultDark.Name)
            .ReturnsAsync(true);

        await settingsViewModel.EditThemeCommand.ExecuteAsync(null);

        Assert.AreEqual(ThemePresets.DefaultLight.Name, settingsViewModel.SelectedTheme.Name);
        StringAssert.Contains(settingsViewModel.StatusMessage, "built-in theme");
    }
}
