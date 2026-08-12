using ModManager.Ui.Services;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class BrowserTabViewModelTests
{
    private BrowserDownloadService downloadService = null!;
    private DownloadItemViewModel? capturedDownload;
    private Func<string, Uri?, Action, DownloadItemViewModel> beginDownload = null!;

    [TestInitialize]
    public void Initialize()
    {
        downloadService = new BrowserDownloadService();
        capturedDownload = null;
        beginDownload = (fileName, sourceUri, cancel) =>
        {
            capturedDownload = new DownloadItemViewModel(fileName, sourceUri, cancel);
            return capturedDownload;
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        downloadService.Dispose();
    }

    private BrowserTabViewModel CreateViewModel() => new(downloadService, beginDownload);

    [TestMethod]
    public void Navigate_WhenAddressIsBareHost_ThenRequestsHttpsUri()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "example.com";
        Uri? requested = null;
        viewModel.NavigationRequested += (_, uri) => requested = uri;

        viewModel.NavigateCommand.Execute(null);

        Assert.AreEqual("https://example.com/", requested?.ToString());
    }

    [TestMethod]
    public void Navigate_WhenAddressContainsSpace_ThenRequestsGoogleSearchUri()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "sims 4 cc";
        Uri? requested = null;
        viewModel.NavigationRequested += (_, uri) => requested = uri;

        viewModel.NavigateCommand.Execute(null);

        Assert.IsNotNull(requested);
        Assert.IsTrue(requested!.ToString().StartsWith("https://www.google.com/search?q=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Navigate_WhenAddressHasNoDotAndIsNotLocalhost_ThenRequestsSearchUriInsteadOfGuessingHost()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "sims";
        Uri? requested = null;
        viewModel.NavigationRequested += (_, uri) => requested = uri;

        viewModel.NavigateCommand.Execute(null);

        Assert.IsNotNull(requested);
        Assert.IsTrue(requested!.ToString().StartsWith("https://www.google.com/search?q=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Navigate_WhenAddressIsSchemeQualified_ThenPassesThroughUnchanged()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "https://example.com/path";
        Uri? requested = null;
        viewModel.NavigationRequested += (_, uri) => requested = uri;

        viewModel.NavigateCommand.Execute(null);

        Assert.AreEqual("https://example.com/path", requested?.ToString());
    }

    [TestMethod]
    public void Navigate_WhenAddressIsEmpty_ThenSetsStatusMessageAndDoesNotNavigate()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "   ";
        bool navigated = false;
        viewModel.NavigationRequested += (_, _) => navigated = true;

        viewModel.NavigateCommand.Execute(null);

        Assert.IsFalse(navigated);
        Assert.AreEqual("Enter a valid URL.", viewModel.StatusMessage);
    }

    [TestMethod]
    public void Navigate_WhenUrlLooksLikeDownload_ThenTakesDownloadPathInsteadOfNavigating()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        // .invalid is reserved by RFC 2606 to never resolve, so the real HTTP call this triggers
        // fails fast on DNS resolution without a live network round trip.
        viewModel.AddressText = "https://invalid.invalid/mod-file.zip";
        bool navigated = false;
        viewModel.NavigationRequested += (_, _) => navigated = true;

        viewModel.NavigateCommand.Execute(null);

        Assert.IsFalse(navigated);
        Assert.IsTrue(viewModel.StatusMessage.StartsWith("Downloading", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OnAdBlocked_WhenSameUrlBlockedTwice_ThenCountAndTooltipGrowOnlyOnce()
    {
        BrowserTabViewModel viewModel = CreateViewModel();

        viewModel.OnAdBlocked("https://ads.example.com/a");
        viewModel.OnAdBlocked("https://ads.example.com/a");

        Assert.AreEqual(1, viewModel.BlockedAds);
        Assert.IsTrue(viewModel.BlockedAdsTooltip.Contains("Blocked ads:", StringComparison.Ordinal));
        Assert.IsTrue(viewModel.BlockedAdsTooltip.Contains("https://ads.example.com/a", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OnAdBlocked_WhenDifferentUrlsBlocked_ThenCountGrowsForEach()
    {
        BrowserTabViewModel viewModel = CreateViewModel();

        viewModel.OnAdBlocked("https://ads.example.com/a");
        viewModel.OnAdBlocked("https://ads.example.com/b");

        Assert.AreEqual(2, viewModel.BlockedAds);
    }

    [TestMethod]
    public void OnNavigationStarted_WhenNoActiveDownload_ThenResetsAdBlockStateAndSetsLoading()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnAdBlocked("https://ads.example.com/a");

        viewModel.OnNavigationStarted();

        Assert.AreEqual(0, viewModel.BlockedAds);
        Assert.AreEqual("No ads blocked", viewModel.BlockedAdsTooltip);
        Assert.IsTrue(viewModel.IsLoading);
        Assert.AreEqual("Loading...", viewModel.StatusMessage);
    }

    [TestMethod]
    public void OnNavigationStarted_WhenDownloadInProgress_ThenLeavesStateUntouched()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnAdBlocked("https://ads.example.com/a");
        viewModel.OnBrowserDownloadStarted("mod.zip");
        string statusBeforeNavigationStarted = viewModel.StatusMessage;

        viewModel.OnNavigationStarted();

        Assert.AreEqual(1, viewModel.BlockedAds);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.AreEqual(statusBeforeNavigationStarted, viewModel.StatusMessage);
    }

    [TestMethod]
    public void OnNavigationCompleted_WhenSuccessful_ThenSetsAddressTitleAndHostStatus()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        Uri topLevelSource = new("https://example.com/page");

        viewModel.OnNavigationCompleted(topLevelSource, isSuccess: true);

        Assert.AreEqual(topLevelSource.ToString(), viewModel.AddressText);
        Assert.AreEqual("example.com", viewModel.Title);
        Assert.AreEqual("example.com", viewModel.StatusMessage);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public void OnNavigationCompleted_WhenFailed_ThenSetsNavigationFailedStatus()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        Uri topLevelSource = new("https://example.com/page");

        viewModel.OnNavigationCompleted(topLevelSource, isSuccess: false);

        Assert.AreEqual("Navigation failed.", viewModel.StatusMessage);
    }

    [TestMethod]
    public void UpdateNavigationState_ThenSetsCanGoBackAndCanGoForward()
    {
        BrowserTabViewModel viewModel = CreateViewModel();

        viewModel.UpdateNavigationState(canGoBack: true, canGoForward: false);
        Assert.IsTrue(viewModel.CanGoBack);
        Assert.IsFalse(viewModel.CanGoForward);

        viewModel.UpdateNavigationState(canGoBack: false, canGoForward: true);
        Assert.IsFalse(viewModel.CanGoBack);
        Assert.IsTrue(viewModel.CanGoForward);
    }

    [TestMethod]
    public void GoHome_ThenRaisesNavigationRequestedWithDefaultHome()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        Uri? requested = null;
        viewModel.NavigationRequested += (_, uri) => requested = uri;

        viewModel.GoHomeCommand.Execute(null);

        Assert.AreEqual(BrowserTabViewModel.DefaultHome, requested);
    }

    [TestMethod]
    public void OnBrowserDownloadStarted_ThenBeginsDownloadAndDisablesDownloadCurrentCommand()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        Assert.IsTrue(viewModel.DownloadCurrentCommand.CanExecute(null));

        viewModel.OnBrowserDownloadStarted("mod.zip");

        Assert.IsNotNull(capturedDownload);
        Assert.AreEqual("mod.zip", capturedDownload!.FileName);
        Assert.IsFalse(viewModel.DownloadCurrentCommand.CanExecute(null));
        Assert.AreEqual("Downloading mod.zip...", viewModel.StatusMessage);
    }

    [TestMethod]
    public void OnBrowserDownloadStarted_ThenAttributesDownloadToCurrentAddressAsModPage()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.AddressText = "https://example.com/mod-page";

        viewModel.OnBrowserDownloadStarted("mod.zip");

        Assert.AreEqual(new Uri("https://example.com/mod-page"), capturedDownload!.ModPageUri);
    }

    [TestMethod]
    public void OnBrowserDownloadUpdated_WhenCompleted_ThenMarksDownloadCompletedAndReenablesCommand()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnBrowserDownloadStarted("mod.zip");

        viewModel.OnBrowserDownloadUpdated(1.0, completed: true, canceled: false, path: @"C:\Downloads\mod.zip");

        Assert.AreEqual(DownloadItemState.Completed, capturedDownload!.State);
        Assert.AreEqual(@"C:\Downloads\mod.zip", capturedDownload.FilePath);
        Assert.AreEqual("Saved mod.zip", viewModel.StatusMessage);
        Assert.IsTrue(viewModel.DownloadCurrentCommand.CanExecute(null));
    }

    [TestMethod]
    public void OnBrowserDownloadUpdated_WhenCanceled_ThenMarksDownloadCanceledAndReenablesCommand()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnBrowserDownloadStarted("mod.zip");

        viewModel.OnBrowserDownloadUpdated(0.4, completed: false, canceled: true, path: null);

        Assert.AreEqual(DownloadItemState.Canceled, capturedDownload!.State);
        Assert.AreEqual("Download canceled.", viewModel.StatusMessage);
        Assert.IsTrue(viewModel.DownloadCurrentCommand.CanExecute(null));
    }

    [TestMethod]
    public void OnBrowserDownloadUpdated_WhenNeitherCompletedNorCanceled_ThenReportsProgressOnly()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnBrowserDownloadStarted("mod.zip");

        viewModel.OnBrowserDownloadUpdated(0.5, completed: false, canceled: false, path: null);

        Assert.AreEqual(0.5, capturedDownload!.Progress);
        Assert.IsFalse(viewModel.DownloadCurrentCommand.CanExecute(null));
        Assert.IsTrue(viewModel.StatusMessage.StartsWith("Downloading...", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OnNewTabRequested_ThenRaisesNewTabRequestedWithSelfAndUri()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        Uri target = new("https://example.com/popup");
        (BrowserTabViewModel Source, Uri Uri)? raised = null;
        viewModel.NewTabRequested += (source, uri) => raised = (source, uri);

        viewModel.OnNewTabRequested(target);

        Assert.IsNotNull(raised);
        Assert.AreSame(viewModel, raised!.Value.Source);
        Assert.AreEqual(target, raised.Value.Uri);
    }

    [TestMethod]
    public void OnBrowserDownloadFailed_ThenMarksDownloadFailedAndReenablesCommand()
    {
        BrowserTabViewModel viewModel = CreateViewModel();
        viewModel.OnBrowserDownloadStarted("mod.zip");

        viewModel.OnBrowserDownloadFailed("Connection reset.");

        Assert.AreEqual(DownloadItemState.Failed, capturedDownload!.State);
        Assert.AreEqual("Connection reset.", capturedDownload.ErrorMessage);
        Assert.AreEqual("Connection reset.", viewModel.StatusMessage);
        Assert.IsTrue(viewModel.DownloadCurrentCommand.CanExecute(null));
    }
}
