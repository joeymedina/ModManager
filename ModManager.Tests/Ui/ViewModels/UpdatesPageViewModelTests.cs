using Moq;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class UpdatesPageViewModelTests
{
    private Mock<IModsFolderUseCase> modsFolderUseCaseMock = null!;
    private Mock<IArchiveInstallService> archiveInstallServiceMock = null!;
    private Mock<IDialogService> dialogServiceMock = null!;
    private Mock<IModSiteUpdateService> updateServiceMock = null!;
    private string sandboxPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandboxPath);

        modsFolderUseCaseMock = new Mock<IModsFolderUseCase>();
        archiveInstallServiceMock = new Mock<IArchiveInstallService>();
        dialogServiceMock = new Mock<IDialogService>();
        updateServiceMock = new Mock<IModSiteUpdateService>();

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

    private ModsPageViewModel CreateModsViewModel()
    {
        string settingsFilePath = Path.Combine(sandboxPath, $"{Guid.NewGuid():N}.json");
        SettingsStore settings = new(settingsFilePath);
        settings.Save(new AppSettings { ModsFolderPath = "C:/Mods" });

        return new ModsPageViewModel(
            modsFolderUseCaseMock.Object,
            archiveInstallServiceMock.Object,
            dialogServiceMock.Object,
            settings);
    }

    private static InstallRecord CreateTrackedRecord(string installId, string? baselineVersion, string relativePath = "Zombie Apocalypse/Main.package")
    {
        UpdateTracking tracking = new(
            "sacrificialmods.com",
            "ZombieApocalypseDownload",
            "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload",
            baselineVersion,
            null,
            DateTime.UtcNow);

        return new InstallRecord(
            installId,
            new InstallSource("browser", tracking.TrackingUrl, null),
            baselineVersion,
            DateTime.UtcNow,
            null,
            [new InstallRecordFile(relativePath, "abc", 1)],
            [],
            tracking);
    }

    // --- CheckForUpdatesAsync ------------------------------------------------

    [TestMethod]
    public async Task CheckForUpdatesAsync_WhenNoInstallsAreTracked_ThenShowsANoTrackedMessageAndDoesNotCallTheUpdateService()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty);

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.IsEmpty(viewModel.Rows);
        StringAssert.Contains(viewModel.StatusMessage, "No mods are linked");
        updateServiceMock.Verify(service => service.CheckAsync(It.IsAny<IReadOnlyList<TrackedMod>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_WhenAnInstallIsTracked_ThenPopulatesARowFromTheResult()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        ModsManifest manifest = ModsManifest.Empty with
        {
            Installs = [record],
            Files = [new ManifestFileEntry(record.Files[0].RelativePath, "Zombie Apocalypse")]
        };
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifest);

        SiteUpdateCheckResult result = new("install-1", SiteUpdateStatus.UpdateAvailable, "2.3.2", null, null, DateTime.UtcNow);
        updateServiceMock
            .Setup(service => service.CheckAsync(It.IsAny<IReadOnlyList<TrackedMod>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SiteUpdateCheckResult>)[result]);

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.HasCount(1, viewModel.Rows);
        UpdateRowViewModel row = viewModel.Rows.Single();
        Assert.AreEqual("Zombie Apocalypse", row.DisplayName);
        Assert.AreEqual("2.3.1", row.InstalledVersion);
        Assert.AreEqual("2.3.2", row.ObservedVersion);
        Assert.IsTrue(row.IsUpdateAvailable);
        StringAssert.Contains(viewModel.StatusMessage, "1 update");
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_WhenNoUpdatesAreFound_ThenStatusMessageReportsUpToDate()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty with { Installs = [record] });

        SiteUpdateCheckResult result = new("install-1", SiteUpdateStatus.UpToDate, "2.3.1", null, null, DateTime.UtcNow);
        updateServiceMock
            .Setup(service => service.CheckAsync(It.IsAny<IReadOnlyList<TrackedMod>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SiteUpdateCheckResult>)[result]);

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusMessage, "up to date");
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_WhenAKeyIsNewlyResolved_ThenPersistsItBackToTheManifest()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty with { Installs = [record] });

        SiteModKey resolvedKey = new("ZombieApocalypseDownload");
        SiteUpdateCheckResult result = new("install-1", SiteUpdateStatus.UpToDate, "2.3.1", null, null, DateTime.UtcNow, resolvedKey);
        updateServiceMock
            .Setup(service => service.CheckAsync(It.IsAny<IReadOnlyList<TrackedMod>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SiteUpdateCheckResult>)[result]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.UpdateInstallTrackingAsync(It.IsAny<string>(), "install-1", It.IsAny<UpdateTracking>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(record));

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        modsFolderUseCaseMock.Verify(
            useCase => useCase.UpdateInstallTrackingAsync(
                It.IsAny<string>(),
                "install-1",
                It.Is<UpdateTracking>(tracking => tracking.SiteModKey == "ZombieApocalypseDownload"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_WhenTheUpdateServiceThrows_ThenSetsAnErrorMessageInsteadOfCrashing()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty with { Installs = [record] });
        updateServiceMock
            .Setup(service => service.CheckAsync(It.IsAny<IReadOnlyList<TrackedMod>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network is down"));

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusMessage, "network is down");
        Assert.IsFalse(viewModel.IsBusy);
    }

    // --- MarkAsCurrentAsync ---------------------------------------------------

    [TestMethod]
    public async Task MarkAsCurrentAsync_WhenRowHasAnObservedVersion_ThenWritesItAsTheNewBaseline()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        var row = new UpdateRowViewModel(record, "Zombie Apocalypse");
        row.ApplyResult(new SiteUpdateCheckResult("install-1", SiteUpdateStatus.UpdateAvailable, "2.3.2", null, null, DateTime.UtcNow));

        modsFolderUseCaseMock
            .Setup(useCase => useCase.UpdateInstallTrackingAsync(It.IsAny<string>(), "install-1", It.IsAny<UpdateTracking>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, UpdateTracking tracking, CancellationToken _) => ArchiveInstallResult<InstallRecord>.Ok(record with { Tracking = tracking }));

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.MarkAsCurrentCommand.ExecuteAsync(row);

        Assert.AreEqual("2.3.2", row.InstalledVersion);
        Assert.AreEqual(SiteUpdateStatus.UpToDate, row.Status);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.UpdateInstallTrackingAsync(
                It.IsAny<string>(),
                "install-1",
                It.Is<UpdateTracking>(tracking => tracking.BaselineVersion == "2.3.2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MarkAsCurrentAsync_WhenNothingHasBeenObservedYet_ThenDoesNothing()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        var row = new UpdateRowViewModel(record, "Zombie Apocalypse");

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.MarkAsCurrentCommand.ExecuteAsync(row);

        modsFolderUseCaseMock.Verify(
            useCase => useCase.UpdateInstallTrackingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UpdateTracking>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task MarkAsCurrentAsync_WhenTheWriteFails_ThenShowsTheFailureMessageAndLeavesTheRowUnchanged()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        var row = new UpdateRowViewModel(record, "Zombie Apocalypse");
        row.ApplyResult(new SiteUpdateCheckResult("install-1", SiteUpdateStatus.UpdateAvailable, "2.3.2", null, null, DateTime.UtcNow));

        modsFolderUseCaseMock
            .Setup(useCase => useCase.UpdateInstallTrackingAsync(It.IsAny<string>(), "install-1", It.IsAny<UpdateTracking>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Fail("Could not find that install."));

        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        await viewModel.MarkAsCurrentCommand.ExecuteAsync(row);

        Assert.AreEqual("Could not find that install.", viewModel.StatusMessage);
        Assert.AreEqual(SiteUpdateStatus.UpdateAvailable, row.Status);
    }

    // --- OpenModPage -----------------------------------------------------------

    [TestMethod]
    public void OpenModPage_WhenRowHasATrackingUrl_ThenRaisesOpenModPageRequestedRatherThanShellingOut()
    {
        InstallRecord record = CreateTrackedRecord("install-1", "2.3.1");
        var row = new UpdateRowViewModel(record, "Zombie Apocalypse");
        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        Uri? raised = null;
        viewModel.OpenModPageRequested += uri => raised = uri;

        viewModel.OpenModPageCommand.Execute(row);

        Assert.AreEqual(new Uri(row.TrackingUrl), raised);
    }

    [TestMethod]
    public void OpenModPage_WhenRowIsNull_ThenDoesNotRaiseOpenModPageRequested()
    {
        var viewModel = new UpdatesPageViewModel(CreateModsViewModel(), updateServiceMock.Object);

        bool raised = false;
        viewModel.OpenModPageRequested += _ => raised = true;

        viewModel.OpenModPageCommand.Execute(null);

        Assert.IsFalse(raised);
    }
}
