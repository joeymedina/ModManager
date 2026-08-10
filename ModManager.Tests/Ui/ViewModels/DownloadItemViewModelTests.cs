using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class DownloadItemViewModelTests
{
    private static DownloadItemViewModel CreateViewModel() =>
        new("mod.zip", new Uri("https://example.com/mod.zip"), () => { });

    [TestMethod]
    public void CanCancel_WhenInProgress_ThenCommandCanExecute()
    {
        DownloadItemViewModel viewModel = CreateViewModel();

        Assert.IsTrue(viewModel.CancelCommand.CanExecute(null));
    }

    [TestMethod]
    public void MarkCompleted_ThenTransitionsStateAndDisablesCancel()
    {
        DownloadItemViewModel viewModel = CreateViewModel();

        viewModel.MarkCompleted(@"C:\Downloads\mod.zip");

        Assert.AreEqual(DownloadItemState.Completed, viewModel.State);
        Assert.AreEqual(1, viewModel.Progress);
        Assert.AreEqual(@"C:\Downloads\mod.zip", viewModel.FilePath);
        Assert.IsFalse(viewModel.CancelCommand.CanExecute(null));
    }

    [TestMethod]
    public void MarkCanceled_ThenTransitionsStateAndDisablesCancel()
    {
        DownloadItemViewModel viewModel = CreateViewModel();

        viewModel.MarkCanceled();

        Assert.AreEqual(DownloadItemState.Canceled, viewModel.State);
        Assert.IsFalse(viewModel.CancelCommand.CanExecute(null));
    }

    [TestMethod]
    public void MarkFailed_ThenTransitionsStateSetsErrorMessageAndDisablesCancel()
    {
        DownloadItemViewModel viewModel = CreateViewModel();

        viewModel.MarkFailed("Connection reset.");

        Assert.AreEqual(DownloadItemState.Failed, viewModel.State);
        Assert.AreEqual("Connection reset.", viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.CancelCommand.CanExecute(null));
    }

    [TestMethod]
    public void Cancel_WhenExecuted_ThenInvokesCancelRequestedCallback()
    {
        bool requested = false;
        DownloadItemViewModel viewModel = new("mod.zip", null, () => requested = true);

        viewModel.CancelCommand.Execute(null);

        Assert.IsTrue(requested);
    }

    [TestMethod]
    public void CanInstall_WhenCompletedWithAllowedExtension_ThenCommandCanExecute()
    {
        DownloadItemViewModel viewModel = CreateViewModel();
        viewModel.MarkCompleted(@"C:\Downloads\mod.zip");

        Assert.IsTrue(viewModel.InstallToModsCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanInstall_WhenCompletedWithDisallowedExtension_ThenCommandCannotExecute()
    {
        DownloadItemViewModel viewModel = CreateViewModel();
        viewModel.MarkCompleted(@"C:\Downloads\readme.txt");

        Assert.IsFalse(viewModel.InstallToModsCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanInstall_WhenNotCompleted_ThenCommandCannotExecute()
    {
        DownloadItemViewModel viewModel = CreateViewModel();

        Assert.IsFalse(viewModel.InstallToModsCommand.CanExecute(null));
    }

    [TestMethod]
    public void InstallToMods_WhenExecuted_ThenRaisesInstallRequestedWithFilePathSourceAndModPage()
    {
        Uri sourceUri = new("https://example.com/mod.zip");
        Uri modPageUri = new("https://example.com/mod-page");
        DownloadItemViewModel viewModel = new("mod.zip", sourceUri, () => { }) { ModPageUri = modPageUri };
        viewModel.MarkCompleted(@"C:\Downloads\mod.zip");

        (string FilePath, Uri? SourceUri, Uri? ModPageUri)? raised = null;
        viewModel.InstallRequested += (filePath, source, modPage) => raised = (filePath, source, modPage);

        viewModel.InstallToModsCommand.Execute(null);

        Assert.IsNotNull(raised);
        Assert.AreEqual(@"C:\Downloads\mod.zip", raised!.Value.FilePath);
        Assert.AreEqual(sourceUri, raised.Value.SourceUri);
        Assert.AreEqual(modPageUri, raised.Value.ModPageUri);
    }
}
