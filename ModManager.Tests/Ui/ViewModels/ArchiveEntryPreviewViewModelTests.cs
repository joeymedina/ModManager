using ModManager.Application.Models;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ArchiveEntryPreviewViewModelTests
{
    [TestMethod]
    public void Constructor_WhenKindIsInstallable_ThenIsInstallableTrueAndLabelIsInstallable()
    {
        ArchiveEntryPreviewViewModel viewModel = new(new ArchiveEntryPreview("foo.package", ArchiveEntryKind.Installable, SelectedByDefault: true));

        Assert.IsTrue(viewModel.IsInstallable);
        Assert.AreEqual("Installable", viewModel.KindLabel);
    }

    [TestMethod]
    public void Constructor_WhenKindIsVariant_ThenIsInstallableTrueAndLabelWarnsToReview()
    {
        ArchiveEntryPreviewViewModel viewModel = new(new ArchiveEntryPreview("foo.package", ArchiveEntryKind.Variant, SelectedByDefault: false));

        Assert.IsTrue(viewModel.IsInstallable);
        Assert.AreEqual("Variant — review before installing", viewModel.KindLabel);
    }

    [TestMethod]
    public void Constructor_WhenKindIsNotInstallable_ThenIsInstallableFalseAndLabelSaysNotAModFile()
    {
        ArchiveEntryPreviewViewModel viewModel = new(new ArchiveEntryPreview("readme.txt", ArchiveEntryKind.NotInstallable, SelectedByDefault: false));

        Assert.IsFalse(viewModel.IsInstallable);
        Assert.AreEqual("Not a mod file", viewModel.KindLabel);
    }

    [TestMethod]
    public void Constructor_WhenSelectedByDefaultIsTrue_ThenIsSelectedSeedsTrue()
    {
        ArchiveEntryPreviewViewModel viewModel = new(new ArchiveEntryPreview("foo.package", ArchiveEntryKind.Installable, SelectedByDefault: true));

        Assert.IsTrue(viewModel.IsSelected);
    }

    [TestMethod]
    public void Constructor_WhenSelectedByDefaultIsFalse_ThenIsSelectedSeedsFalse()
    {
        ArchiveEntryPreviewViewModel viewModel = new(new ArchiveEntryPreview("foo.package", ArchiveEntryKind.Variant, SelectedByDefault: false));

        Assert.IsFalse(viewModel.IsSelected);
    }
}
