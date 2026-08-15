using Moq;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ModsPageViewModelTests
{
    private Mock<IModsFolderUseCase> modsFolderUseCaseMock = null!;
    private Mock<IArchiveInstallService> archiveInstallServiceMock = null!;
    private Mock<IDialogService> dialogServiceMock = null!;
    private string sandboxPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandboxPath);

        modsFolderUseCaseMock = new Mock<IModsFolderUseCase>();
        archiveInstallServiceMock = new Mock<IArchiveInstallService>();
        dialogServiceMock = new Mock<IDialogService>();

        // Every constructor call and most command paths resolve the layout at least once — a
        // permissive default keeps every test from having to restate it.
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

    private static ModFile CreateModFile(string relativePath, ModFileState state) =>
        new(relativePath, state, SizeBytes: 100, ModifiedUtc: DateTime.UtcNow);

    private ModsPageViewModel CreateViewModel(string modsFolderPath) =>
        CreateViewModelWithSettingsPath(modsFolderPath).ViewModel;

    private (ModsPageViewModel ViewModel, string SettingsFilePath) CreateViewModelWithSettingsPath(string modsFolderPath)
    {
        string settingsFilePath = Path.Combine(sandboxPath, $"{Guid.NewGuid():N}.json");
        SettingsStore settings = new(settingsFilePath);
        settings.Save(new AppSettings { ModsFolderPath = modsFolderPath });

        ModsPageViewModel viewModel = new(
            modsFolderUseCaseMock.Object,
            archiveInstallServiceMock.Object,
            dialogServiceMock.Object,
            settings);

        return (viewModel, settingsFilePath);
    }

    // --- ToggleFileAsync ------------------------------------------------

    [TestMethod]
    public async Task ToggleFileAsync_WhenEnablingSucceeds_ThenPatchesRowInPlaceWithoutReload()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("Extras/foo.package", ModFileState.Disabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.EnableAsync("C:/Mods", It.Is<IReadOnlyList<string>>(paths => paths.Single() == "Extras/foo.package"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFileFailure>)[]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        ModFileViewModel file = viewModel.Files.Single();

        await viewModel.ToggleFileCommand.ExecuteAsync(file);

        Assert.AreEqual(ModFileState.Enabled, file.State);
        Assert.AreEqual("Enabled", file.StatusText);
        Assert.Contains("Enabled foo.package", viewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ToggleFileAsync_WhenStatusFilterIsActive_ThenReAppliesFilterAndDropsRowThatNoLongerMatches()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("foo.package", ModFileState.Disabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.EnableAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFileFailure>)[]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.StatusFilter = ModsStatusFilter.Disabled;
        ModFileViewModel file = viewModel.Files.Single();

        await viewModel.ToggleFileCommand.ExecuteAsync(file);

        Assert.AreEqual(ModFileState.Enabled, file.State);
        Assert.DoesNotContain(file, viewModel.Files);
    }

    [TestMethod]
    public async Task ToggleFileAsync_WhenDisableFails_ThenLeavesFileStateUnchangedAndSetsErrorMessage()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("foo.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.DisableAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFileFailure>)[new ModFileFailure("foo.package", "a copy exists in both folders")]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        ModFileViewModel file = viewModel.Files.Single();

        await viewModel.ToggleFileCommand.ExecuteAsync(file);

        Assert.AreEqual(ModFileState.Enabled, file.State);
        Assert.Contains("Could not disable foo.package", viewModel.ErrorMessage);
        Assert.Contains("a copy exists in both folders", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- ApplyFilter ------------------------------------------------------

    [TestMethod]
    public async Task ApplyFilter_WhenSearchTextMatchesAFolderName_ThenIncludesFilesUnderThatFolder()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                CreateModFile("Extras/foo.package", ModFileState.Enabled),
                CreateModFile("bar.package", ModFileState.Enabled),
            ]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "extras";

        ModFileViewModel remaining = viewModel.Files.Single();
        Assert.AreEqual("Extras/foo.package", remaining.RelativePath);
    }

    [TestMethod]
    public async Task ApplyFilter_WhenStatusFilterIsEnabled_ThenOnlyEnabledFilesRemain()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                CreateModFile("foo.package", ModFileState.Enabled),
                CreateModFile("bar.package", ModFileState.Disabled),
            ]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.StatusFilter = ModsStatusFilter.Enabled;

        ModFileViewModel remaining = viewModel.Files.Single();
        Assert.AreEqual(ModFileState.Enabled, remaining.State);
    }

    [TestMethod]
    public async Task ApplyFilter_WhenGroupModeAndSearchTextIsSet_ThenGroupTreeIgnoresTheSearchBox()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                CreateModFile("Alpha.package", ModFileState.Enabled),
                CreateModFile("Beta.package", ModFileState.Enabled),
            ]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadGroupsAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModGroup>)[new ModGroup("group-1", "MyGroup", ["Alpha.package", "Beta.package"])]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.ListMode = ModsListMode.Group;

        viewModel.SearchText = "no-such-mod";

        Assert.IsEmpty(viewModel.Files);
        ModGroupNodeViewModel group = viewModel.GroupTree.Single();
        Assert.HasCount(2, group.Children);
    }

    [TestMethod]
    public async Task ApplyFilter_WhenCategoryFilterIsSet_ThenOnlyMatchingFilesShow()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                new ModFile("foo.package", ModFileState.Enabled, SizeBytes: 100, ModifiedUtc: DateTime.UtcNow, Category: "Scripts"),
                new ModFile("bar.package", ModFileState.Enabled, SizeBytes: 100, ModifiedUtc: DateTime.UtcNow, Category: "CAS"),
            ]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.CategoryFilter = "Scripts";

        ModFileViewModel remaining = viewModel.Files.Single();
        Assert.AreEqual("foo.package", remaining.RelativePath);
    }

    [TestMethod]
    public async Task ApplyFilter_WhenCategoryFilterIsUncategorized_ThenOnlyFilesWithNoCategoryShow()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                new ModFile("foo.package", ModFileState.Enabled, SizeBytes: 100, ModifiedUtc: DateTime.UtcNow, Category: "Scripts"),
                new ModFile("bar.package", ModFileState.Enabled, SizeBytes: 100, ModifiedUtc: DateTime.UtcNow),
            ]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.CategoryFilter = "Uncategorized";

        ModFileViewModel remaining = viewModel.Files.Single();
        Assert.AreEqual("bar.package", remaining.RelativePath);
    }

    // --- Tree selection syncing -------------------------------------------

    [TestMethod]
    public async Task SelectedTreeNodes_WhenFolderNodeIsSelected_ThenEveryDescendantFileIsAddedToSelectedFiles()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                CreateModFile("Extras/a.package", ModFileState.Enabled),
                CreateModFile("Extras/b.package", ModFileState.Enabled),
                CreateModFile("root.package", ModFileState.Enabled),
            ]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        ModTreeNodeViewModel folderNode = viewModel.FolderTree.Single(node => node.IsFolder);

        viewModel.SelectedTreeNodes.Add(folderNode);

        Assert.HasCount(2, viewModel.SelectedFiles);
        Assert.IsTrue(viewModel.SelectedFiles.All(file => file.Folder == "Extras"));
        Assert.AreEqual(2, viewModel.SelectedCount);
    }

    [TestMethod]
    public async Task SelectedGroupNodes_WhenGroupNodeIsSelected_ThenMemberFilesAreAddedAndMissingMembersAreSkipped()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("Alpha.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadGroupsAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModGroup>)[new ModGroup("group-1", "MyGroup", ["Alpha.package", "Beta.package"])]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        ModGroupNodeViewModel groupNode = viewModel.GroupTree.Single();

        viewModel.SelectedGroupNodes.Add(groupNode);

        ModFileViewModel selected = viewModel.SelectedFiles.Single();
        Assert.AreEqual("Alpha.package", selected.RelativePath);
    }

    // --- SetModsFolderAsync -------------------------------------------------

    [TestMethod]
    public async Task SetModsFolderAsync_WhenCalled_ThenPersistsThePathAndReloadsFiles()
    {
        (ModsPageViewModel viewModel, string settingsFilePath) = CreateViewModelWithSettingsPath("C:/Mods");

        await viewModel.SetModsFolderAsync("D:/NewMods");

        Assert.AreEqual("D:/NewMods", viewModel.ModsFolderPath);
        Assert.AreEqual("D:/NewMods.Disabled", viewModel.DisabledModsFolderPath);
        Assert.AreEqual("D:/NewMods", new SettingsStore(settingsFilePath).Load().ModsFolderPath);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync("D:/NewMods", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Bulk-action and use-case failures surfacing without throwing -----

    [TestMethod]
    public async Task EnableSelectedAsync_WhenUseCaseReturnsFailures_ThenSetsErrorMessageInsteadOfThrowing()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)
            [
                CreateModFile("a.package", ModFileState.Disabled),
                CreateModFile("b.package", ModFileState.Disabled),
            ]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.EnableAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFileFailure>)[new ModFileFailure("a.package", "a copy exists in both folders")]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        foreach (ModFileViewModel file in viewModel.Files)
        {
            viewModel.SelectedFiles.Add(file);
        }

        await viewModel.EnableSelectedCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.ErrorMessage, "1 file(s) failed");
        StringAssert.Contains(viewModel.ErrorMessage, "a.package: a copy exists in both folders");
        StringAssert.Contains(viewModel.StatusMessage, "Enabled 1 file(s).");
    }

    [TestMethod]
    public async Task EnableSelectedAsync_WhenNothingIsSelected_ThenDoesNotCallTheUseCase()
    {
        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.EnableSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual("Select one or more files first.", viewModel.StatusMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.EnableAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task AdoptSelectedAsync_WhenAdoptFails_ThenSetsErrorMessageVerbatimAndDoesNotReload()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.AdoptAsync(
                "C:/Mods", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Fail("A file with that name already exists."));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());
        dialogServiceMock
            .Setup(dialog => dialog.ShowAsync(It.IsAny<string>(), AppDialog.Adopt, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await viewModel.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual("A file with that name already exists.", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task AddToGroupAsync_WhenGroupNameIsEmpty_ThenSetsErrorMessageWithoutCallingTheUseCase()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());
        dialogServiceMock
            .Setup(dialog => dialog.ShowAsync(It.IsAny<string>(), AppDialog.AddToGroup, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await viewModel.AddToGroupCommand.ExecuteAsync(null);

        Assert.AreEqual("A group needs a name.", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.AddToGroupAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task AddToGroupAsync_WhenUseCaseFails_ThenSetsErrorMessageVerbatimAndDoesNotReload()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.AddToGroupAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), "MyGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<ModGroup>.Fail("A group with that name already exists."));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());
        // Stands in for the dialog binding: a real AddToGroupDialogContent two-way binds
        // GroupNameInput to a text box, so confirming carries whatever the user typed.
        dialogServiceMock
            .Setup(dialog => dialog.ShowAsync(It.IsAny<string>(), AppDialog.AddToGroup, It.IsAny<object>(), It.IsAny<string>()))
            .Callback(() => viewModel.GroupNameInput = "MyGroup")
            .ReturnsAsync(true);

        await viewModel.AddToGroupCommand.ExecuteAsync(null);

        Assert.AreEqual("A group with that name already exists.", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenCategoryInputIsBlank_ThenClearsCategoryWithoutAnError()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.SetCategoryAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<string?>.Ok(null));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());
        dialogServiceMock
            .Setup(dialog => dialog.ShowAsync(It.IsAny<string>(), AppDialog.SetCategory, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await viewModel.SetCategoryCommand.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
        StringAssert.Contains(viewModel.StatusMessage, "Cleared category");
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenUseCaseFails_ThenSetsErrorMessageVerbatimAndDoesNotReload()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.SetCategoryAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), "Scripts", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<string?>.Fail("Something went wrong."));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());
        // Stands in for the dialog binding: a real SetCategoryDialogContent two-way binds
        // CategoryInput to a text box, so confirming carries whatever the user typed.
        dialogServiceMock
            .Setup(dialog => dialog.ShowAsync(It.IsAny<string>(), AppDialog.SetCategory, It.IsAny<object>(), It.IsAny<string>()))
            .Callback(() => viewModel.CategoryInput = "Scripts")
            .ReturnsAsync(true);

        await viewModel.SetCategoryCommand.ExecuteAsync(null);

        Assert.AreEqual("Something went wrong.", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- DeleteSelectedAsync confirmation gate -----------------------------

    [TestMethod]
    public async Task DeleteSelectedAsync_WhenConfirmed_ThenDeletesAndReloads()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        modsFolderUseCaseMock
            .Setup(useCase => useCase.DeleteAsync("C:/Mods", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFileFailure>)[]);
        dialogServiceMock
            .Setup(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ReturnsAsync(true);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        modsFolderUseCaseMock.Verify(
            useCase => useCase.DeleteAsync("C:/Mods", It.Is<IReadOnlyList<string>>(paths => paths.Single() == "a.package"), It.IsAny<CancellationToken>()),
            Times.Once);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        StringAssert.Contains(viewModel.StatusMessage, "Deleted 1 file(s).");
    }

    [TestMethod]
    public async Task DeleteSelectedAsync_WhenNotConfirmed_ThenDoesNotDeleteOrReload()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModFile>)[CreateModFile("a.package", ModFileState.Enabled)]);
        dialogServiceMock
            .Setup(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ReturnsAsync(false);

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFiles.Add(viewModel.Files.Single());

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        modsFolderUseCaseMock.Verify(
            useCase => useCase.DeleteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- RefreshAsync guard rails -------------------------------------------

    [TestMethod]
    public async Task RefreshAsync_WhenLoadFilesThrows_ThenSetsErrorMessageAndDoesNotPropagate()
    {
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadFilesAsync("C:/Mods", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk error"));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.ErrorMessage, "Failed to load mods");
        StringAssert.Contains(viewModel.ErrorMessage, "disk error");
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task RefreshAsync_WhenModsFolderPathIsEmpty_ThenSetsErrorMessageWithoutCallingTheUseCase()
    {
        ModsPageViewModel viewModel = CreateViewModel(string.Empty);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual("No mods folder is set. Choose one in Settings.", viewModel.ErrorMessage);
        modsFolderUseCaseMock.Verify(
            useCase => useCase.LoadFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- OpenModPage -----------------------------------------------------------

    [TestMethod]
    public void OpenModPage_WhenDetailFileHasAModPageUrl_ThenRaisesOpenModPageRequested()
    {
        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        viewModel.DetailFile = new ModFileViewModel(new ModFile("Main.package", ModFileState.Enabled, 1, DateTime.UtcNow, ModPageUrl: "https://sacrificialmods.com/downloads.html"));

        Uri? raised = null;
        viewModel.OpenModPageRequested += uri => raised = uri;

        viewModel.OpenModPageCommand.Execute(null);

        Assert.AreEqual(new Uri("https://sacrificialmods.com/downloads.html"), raised);
    }

    [TestMethod]
    public void OpenModPage_WhenDetailFileHasNoModPageUrl_ThenDoesNotRaiseOpenModPageRequested()
    {
        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        viewModel.DetailFile = new ModFileViewModel(new ModFile("Main.package", ModFileState.Enabled, 1, DateTime.UtcNow));

        bool raised = false;
        viewModel.OpenModPageRequested += _ => raised = true;

        viewModel.OpenModPageCommand.Execute(null);

        Assert.IsFalse(raised);
    }

    // --- ConfirmInstallAsync: supersede + legacy-rename prompts ------------

    private static ArchiveInstallResult<ArchivePreview> CreateSingleFilePreview(string entryName = "Main.package") =>
        ArchiveInstallResult<ArchivePreview>.Ok(new ArchivePreview([new ArchiveEntryPreview(entryName, ArchiveEntryKind.Installable, SelectedByDefault: true)]));

    private static InstallRecord CreateMatchedRecord(string installId, string folderName, string version) =>
        new(
            installId,
            new InstallSource("browser", "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", null),
            version,
            DateTime.UtcNow,
            null,
            [new InstallRecordFile($"{folderName}/Main.package", "abc", 1)],
            [],
            new UpdateTracking("sacrificialmods.com", "ZombieApocalypseDownload", "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", version, null, DateTime.UtcNow));

    [TestMethod]
    public async Task BeginInstallFromFile_WhenAMatchingTrackedInstallExistsAndUpdateIsConfirmed_ThenInstallsWithSupersedes()
    {
        InstallRecord matched = CreateMatchedRecord("install-1", "Zombie Apocalypse", "2.3.1");
        Uri modPageUri = new("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload");

        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty with
            {
                Installs = [matched],
                Files = [new ManifestFileEntry(matched.Files[0].RelativePath, "Zombie Apocalypse")]
            });
        modsFolderUseCaseMock
            .Setup(useCase => useCase.FindMatchingTrackedInstallAsync(It.IsAny<string>(), It.IsAny<ModKeyHints>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackedMod(matched, "Zombie Apocalypse"));
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        dialogServiceMock
            .Setup(service => service.ConfirmAsync("Update existing mod?", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        dialogServiceMock
            .Setup(service => service.ConfirmAsync("Rename mod folder?", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(false);
        archiveInstallServiceMock
            .Setup(service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(matched));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, modPageUri);

        archiveInstallServiceMock.Verify(
            service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), matched, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task BeginInstallFromFile_WhenAMatchingTrackedInstallExistsButUpdateIsDeclined_ThenInstallsAsNew()
    {
        InstallRecord matched = CreateMatchedRecord("install-1", "Zombie Apocalypse", "2.3.1");
        Uri modPageUri = new("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload");

        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.LoadManifestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModsManifest.Empty with { Installs = [matched] });
        modsFolderUseCaseMock
            .Setup(useCase => useCase.FindMatchingTrackedInstallAsync(It.IsAny<string>(), It.IsAny<ModKeyHints>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackedMod(matched, "Zombie Apocalypse"));
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        dialogServiceMock
            .Setup(service => service.ConfirmAsync("Update existing mod?", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(false);
        archiveInstallServiceMock
            .Setup(service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(matched));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, modPageUri);

        archiveInstallServiceMock.Verify(
            service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
        dialogServiceMock.Verify(service => service.ConfirmAsync("Rename mod folder?", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task BeginInstallFromFile_WhenNoMatchingTrackedInstallExists_ThenInstallsAsNewWithoutPrompting()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.FindMatchingTrackedInstallAsync(It.IsAny<string>(), It.IsAny<ModKeyHints>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackedMod?)null);
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        archiveInstallServiceMock
            .Setup(service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(CreateMatchedRecord("install-2", "New Mod", "1.0")));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, new Uri("https://example.com/mod"));

        dialogServiceMock.Verify(service => service.ConfirmAsync("Update existing mod?", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        archiveInstallServiceMock.Verify(
            service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- ConfirmInstallAsync: install-dialog version/mod-page-URL fields ---

    [TestMethod]
    public async Task PreviewInstallAsync_WhenABrowserDownloadCapturedAModPageUrl_ThenPrefillsInstallModPageUrlInput()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());

        // Captured the instant the (mocked) dialog is about to show — this is what the user would
        // actually see in the field, before either outcome (confirm or cancel) resets the panel.
        string? prefilledValue = null;
        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .Returns(() =>
            {
                prefilledValue = viewModel.InstallModPageUrlInput;
                return Task.FromResult(false);
            });

        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, new Uri("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload"));

        Assert.AreEqual("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", prefilledValue);
    }

    [TestMethod]
    public async Task PreviewInstallAsync_WhenTheModPageUrlResolvesAndVersionIsEmpty_ThenPrefillsTheFetchedVersion()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.TryFetchCurrentVersionAsync(
                "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("2.3.1");

        string? versionAtDialogTime = null;
        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .Returns(() =>
            {
                versionAtDialogTime = viewModel.InstallVersionInput;
                return Task.FromResult(false);
            });

        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, new Uri("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload"));

        Assert.AreEqual("2.3.1", versionAtDialogTime);
    }

    [TestMethod]
    public async Task PreviewInstallAsync_WhenTheDialogIsAlreadyOpenAndAVersionIsTyped_ThenReRunningPreviewDoesNotOverwriteIt()
    {
        // Models editing the archive path (or re-Browsing) while the install dialog is already open —
        // PreviewIfPathChangedAsync and BrowseForArchiveAsync both call PreviewInstallAsync directly,
        // without going through RunInstallFlowAsync's reset (that reset only makes sense at the start
        // of a brand-new install session, e.g. from BeginInstallFromFile, where there's nothing yet to
        // preserve). A version the user already typed for this session must survive a re-preview.
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.TryFetchCurrentVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("2.3.2");

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        viewModel.ArchivePathToInstall = "C:/Downloads/archive.zip";
        viewModel.InstallModPageUrlInput = "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload";
        viewModel.InstallVersionInput = "2.3.1-beta";

        await viewModel.PreviewInstallCommand.ExecuteAsync(null);

        Assert.AreEqual("2.3.1-beta", viewModel.InstallVersionInput);
    }

    [TestMethod]
    public async Task PreviewInstallAsync_WhenNoModPageUrlWasCaptured_ThenDoesNotAttemptAFetch()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip");

        modsFolderUseCaseMock.Verify(
            useCase => useCase.TryFetchCurrentVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.AreEqual(string.Empty, viewModel.InstallVersionInput);
    }

    [TestMethod]
    public async Task ConfirmInstallAsync_WhenAVersionIsTyped_ThenPassesItToInstallAsync()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.FindMatchingTrackedInstallAsync(It.IsAny<string>(), It.IsAny<ModKeyHints>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackedMod?)null);
        archiveInstallServiceMock
            .Setup(service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(CreateMatchedRecord("install-2", "New Mod", "2.3.1")));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .Returns(() =>
            {
                // Models the user typing into the dialog's bound TextBox while it's open, before
                // clicking "Install" — which is what makes ShowAsync return true.
                viewModel.InstallVersionInput = "2.3.1";
                return Task.FromResult(true);
            });

        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip");

        archiveInstallServiceMock.Verify(
            service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), "2.3.1", It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ConfirmInstallAsync_WhenTheCapturedModPageUrlIsEdited_ThenUsesTheEditedValueNotTheCapturedOne()
    {
        archiveInstallServiceMock
            .Setup(service => service.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSingleFilePreview());
        modsFolderUseCaseMock
            .Setup(useCase => useCase.FindMatchingTrackedInstallAsync(It.IsAny<string>(), It.IsAny<ModKeyHints>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackedMod?)null);
        archiveInstallServiceMock
            .Setup(service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<InstallSource>(), It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArchiveInstallResult<InstallRecord>.Ok(CreateMatchedRecord("install-2", "New Mod", "1.0")));

        ModsPageViewModel viewModel = CreateViewModel("C:/Mods");
        dialogServiceMock
            .Setup(service => service.ShowAsync(It.IsAny<string>(), AppDialog.Install, It.IsAny<object>(), It.IsAny<string>()))
            .Returns(() =>
            {
                // The captured URL (a search-results page) was wrong; the user corrects it before confirming.
                viewModel.InstallModPageUrlInput = "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload";
                return Task.FromResult(true);
            });

        await viewModel.BeginInstallFromFile("C:/Downloads/archive.zip", null, new Uri("https://sacrificialmods.com/search?q=zombie"));

        archiveInstallServiceMock.Verify(
            service => service.InstallAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<ModsFolderLayout>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.Is<InstallSource>(source => source.ModPageUrl == "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload"),
                It.IsAny<string?>(), It.IsAny<InstallRecord?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
