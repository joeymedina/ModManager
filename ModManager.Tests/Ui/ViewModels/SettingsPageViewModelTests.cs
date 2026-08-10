using Moq;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
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

        return new ModsPageViewModel(
            modsFolderUseCaseMock.Object,
            archiveInstallServiceMock.Object,
            modsDialogServiceMock.Object,
            settings);
    }

    // --- Constructor --------------------------------------------------------

    [TestMethod]
    public void Constructor_WhenCalled_ThenSeedsModsFolderPathFromWrappedModsPageViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");

        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);

        Assert.AreEqual("C:/Mods", settingsViewModel.ModsFolderPath);
    }

    // --- DisabledModsFolderPath ----------------------------------------------

    [TestMethod]
    public void DisabledModsFolderPath_WhenRead_ThenForwardsFromWrappedModsPageViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);

        Assert.AreEqual(mods.DisabledModsFolderPath, settingsViewModel.DisabledModsFolderPath);
    }

    [TestMethod]
    public async Task DisabledModsFolderPath_WhenModsViewModelChangesFolder_ThenRaisesPropertyChangedOnSettingsViewModel()
    {
        ModsPageViewModel mods = CreateModsViewModel("C:/Mods");
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);
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
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);
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
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);
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
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);
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
        SettingsPageViewModel settingsViewModel = new(mods, settingsDialogServiceMock.Object);
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
}
