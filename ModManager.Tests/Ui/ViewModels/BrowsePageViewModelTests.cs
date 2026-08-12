using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class BrowsePageViewModelTests
{
    private BrowserDownloadService downloadService = null!;

    [TestInitialize]
    public void Initialize()
    {
        downloadService = new BrowserDownloadService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        downloadService.Dispose();
    }

    private BrowsePageViewModel CreateViewModel() => new(downloadService);

    [TestMethod]
    public void Constructor_ThenCreatesSingleInitialTabAndSelectsIt()
    {
        BrowsePageViewModel viewModel = CreateViewModel();

        Assert.HasCount(1, viewModel.Tabs);
        Assert.AreSame(viewModel.Tabs[0], viewModel.SelectedTab);
        Assert.IsTrue(viewModel.Tabs[0].IsSelected);
    }

    [TestMethod]
    public void NewTab_ThenAddsAnotherTabAndSelectsIt()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel firstTab = viewModel.Tabs[0];

        viewModel.NewTabCommand.Execute(null);

        Assert.HasCount(2, viewModel.Tabs);
        Assert.Contains(firstTab, viewModel.Tabs);
        Assert.AreSame(viewModel.Tabs[1], viewModel.SelectedTab);
    }

    [TestMethod]
    public void SelectTab_ThenChangesSelectedTabAndTogglesIsSelectedOnBoth()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel firstTab = viewModel.Tabs[0];
        viewModel.NewTabCommand.Execute(null);
        BrowserTabViewModel secondTab = viewModel.Tabs[1];

        viewModel.SelectTabCommand.Execute(firstTab);

        Assert.AreSame(firstTab, viewModel.SelectedTab);
        Assert.IsTrue(firstTab.IsSelected);
        Assert.IsFalse(secondTab.IsSelected);
    }

    [TestMethod]
    public void CloseTab_WhenOnlyOneTabRemains_ThenIsNoOp()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel onlyTab = viewModel.Tabs[0];

        onlyTab.CloseCommand.Execute(null);

        Assert.HasCount(1, viewModel.Tabs);
        Assert.AreSame(onlyTab, viewModel.SelectedTab);
    }

    [TestMethod]
    public void CloseTab_WhenClosingSelectedTabWithMultipleTabs_ThenReassignsSelectedTabToNeighbor()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel firstTab = viewModel.Tabs[0];
        viewModel.NewTabCommand.Execute(null);
        BrowserTabViewModel secondTab = viewModel.Tabs[1];
        viewModel.SelectTabCommand.Execute(secondTab);

        secondTab.CloseCommand.Execute(null);

        Assert.HasCount(1, viewModel.Tabs);
        Assert.AreSame(firstTab, viewModel.SelectedTab);
        Assert.IsNotNull(viewModel.SelectedTab);
    }

    [TestMethod]
    public void CloseTab_WhenClosingUnselectedTab_ThenSelectionIsUnchanged()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel firstTab = viewModel.Tabs[0];
        viewModel.NewTabCommand.Execute(null);
        BrowserTabViewModel secondTab = viewModel.Tabs[1];
        viewModel.SelectTabCommand.Execute(firstTab);

        secondTab.CloseCommand.Execute(null);

        Assert.HasCount(1, viewModel.Tabs);
        Assert.AreSame(firstTab, viewModel.SelectedTab);
    }

    [TestMethod]
    public void TabNewTabRequested_ThenOpensAndSelectsNewTabAtTargetUri()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel sourceTab = viewModel.Tabs[0];
        Uri target = new("https://example.com/popup");

        sourceTab.OnNewTabRequested(target);

        Assert.HasCount(2, viewModel.Tabs);
        BrowserTabViewModel newTab = viewModel.Tabs[1];
        Assert.AreSame(newTab, viewModel.SelectedTab);
        Assert.AreEqual(target.ToString(), newTab.AddressText);
    }

    [TestMethod]
    public void CloseTab_WhenTabHadRequestedNewTabs_ThenClosingOriginalTabDoesNotAffectOpenedTab()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel sourceTab = viewModel.Tabs[0];
        sourceTab.OnNewTabRequested(new Uri("https://example.com/popup"));
        BrowserTabViewModel openedTab = viewModel.Tabs[1];
        viewModel.SelectTabCommand.Execute(sourceTab);

        sourceTab.CloseCommand.Execute(null);

        Assert.HasCount(1, viewModel.Tabs);
        Assert.AreSame(openedTab, viewModel.Tabs[0]);
    }

    [TestMethod]
    public void BeginDownload_WhenTabStartsDownload_ThenSurfacesInDownloadsAtIndexZero()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel tab = viewModel.SelectedTab!;

        tab.OnBrowserDownloadStarted("mod.zip");

        Assert.HasCount(1, viewModel.Downloads);
        Assert.AreEqual("mod.zip", viewModel.Downloads[0].FileName);
    }

    [TestMethod]
    public void BeginDownload_WhenSecondDownloadStarted_ThenNewestIsInsertedAtIndexZero()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel tab = viewModel.SelectedTab!;
        tab.OnBrowserDownloadStarted("first.zip");
        tab.OnBrowserDownloadUpdated(1.0, completed: true, canceled: false, path: @"C:\Downloads\first.zip");

        tab.OnBrowserDownloadStarted("second.zip");

        Assert.HasCount(2, viewModel.Downloads);
        Assert.AreEqual("second.zip", viewModel.Downloads[0].FileName);
        Assert.AreEqual("first.zip", viewModel.Downloads[1].FileName);
    }

    [TestMethod]
    public void BeginDownload_WhenInstallingCompletedDownload_ThenRaisesInstallRequestedOnPage()
    {
        BrowsePageViewModel viewModel = CreateViewModel();
        BrowserTabViewModel tab = viewModel.SelectedTab!;
        tab.OnBrowserDownloadStarted("mod.zip");
        tab.OnBrowserDownloadUpdated(1.0, completed: true, canceled: false, path: @"C:\Downloads\mod.zip");

        (string FilePath, Uri? SourceUri, Uri? ModPageUri)? raised = null;
        viewModel.InstallRequested += (filePath, sourceUri, modPageUri) => raised = (filePath, sourceUri, modPageUri);

        DownloadItemViewModel download = viewModel.Downloads[0];
        download.InstallToModsCommand.Execute(null);

        Assert.IsNotNull(raised);
        Assert.AreEqual(@"C:\Downloads\mod.zip", raised!.Value.FilePath);
    }
}
