using System.ComponentModel;
using ModManager.Application.Models;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ModFileViewModelTests
{
    private static ModFile CreateModFile(
        string relativePath,
        ModFileState state = ModFileState.Enabled,
        long sizeBytes = 0,
        bool isConflicted = false) =>
        new(relativePath, state, sizeBytes, DateTime.UtcNow, isConflicted);

    // --- FormatSize / SizeText ---------------------------------------------

    [TestMethod]
    public void SizeText_WhenBelowOneKilobyte_ThenShowsWholeBytes()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 500));

        Assert.AreEqual("500 B", viewModel.SizeText);
    }

    [TestMethod]
    public void SizeText_WhenExactlyOneKilobyte_ThenSwitchesToKilobyteUnit()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 1024));

        Assert.AreEqual("1 KB", viewModel.SizeText);
    }

    [TestMethod]
    public void SizeText_WhenFractionalKilobytes_ThenRoundsToOneDecimalPlace()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 1536));

        Assert.AreEqual("1.5 KB", viewModel.SizeText);
    }

    [TestMethod]
    public void SizeText_WhenExactlyOneMegabyte_ThenSwitchesToMegabyteUnit()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 1024L * 1024));

        Assert.AreEqual("1 MB", viewModel.SizeText);
    }

    [TestMethod]
    public void SizeText_WhenExactlyOneGigabyte_ThenSwitchesToGigabyteUnitAndStopsThere()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 1024L * 1024 * 1024 * 1024));

        Assert.AreEqual("1024 GB", viewModel.SizeText);
    }

    // --- Apply() path derivation ---------------------------------------------

    [TestMethod]
    public void Apply_WhenRelativePathUsesBackslashes_ThenFolderIsNormalizedToForwardSlashes()
    {
        ModFileViewModel viewModel = new(CreateModFile(@"Extras\Sub\foo.package"));

        Assert.AreEqual("foo.package", viewModel.Name);
        Assert.AreEqual("Extras/Sub", viewModel.Folder);
        Assert.AreEqual(".package", viewModel.Extension);
    }

    [TestMethod]
    public void Apply_WhenFileIsAtRoot_ThenFolderIsEmptyAndFolderTextIsRootPlaceholder()
    {
        ModFileViewModel viewModel = new(CreateModFile("foo.package"));

        Assert.AreEqual(string.Empty, viewModel.Folder);
        Assert.AreEqual("(root)", viewModel.FolderText);
    }

    [TestMethod]
    public void Apply_WhenFileIsInAFolder_ThenFolderTextIsTheFolderPath()
    {
        ModFileViewModel viewModel = new(CreateModFile("Extras/foo.package"));

        Assert.AreEqual("Extras", viewModel.FolderText);
    }

    // --- StatusText precedence -----------------------------------------------

    [TestMethod]
    public void Refresh_WhenConflictedAndEnabled_ThenStatusTextIsConflicted()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", ModFileState.Enabled, isConflicted: true));

        Assert.AreEqual("Conflicted", viewModel.StatusText);
    }

    [TestMethod]
    public void Refresh_WhenNotConflictedAndEnabled_ThenStatusTextIsEnabled()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", ModFileState.Enabled));

        Assert.AreEqual("Enabled", viewModel.StatusText);
    }

    [TestMethod]
    public void Refresh_WhenNotConflictedAndDisabled_ThenStatusTextIsDisabled()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", ModFileState.Disabled));

        Assert.AreEqual("Disabled", viewModel.StatusText);
    }

    // --- HasDepthWarning -------------------------------------------------------

    [TestMethod]
    public void Apply_WhenTs4ScriptIsNestedInASubfolder_ThenHasDepthWarningIsTrue()
    {
        ModFileViewModel viewModel = new(CreateModFile("Scripts/Sub/foo.ts4script"));

        Assert.IsTrue(viewModel.HasDepthWarning);
    }

    [TestMethod]
    public void Apply_WhenTs4ScriptIsOneFolderDeep_ThenHasDepthWarningIsFalse()
    {
        ModFileViewModel viewModel = new(CreateModFile("Scripts/foo.ts4script"));

        Assert.IsFalse(viewModel.HasDepthWarning);
    }

    [TestMethod]
    public void Apply_WhenTs4ScriptIsAtRoot_ThenHasDepthWarningIsFalse()
    {
        ModFileViewModel viewModel = new(CreateModFile("foo.ts4script"));

        Assert.IsFalse(viewModel.HasDepthWarning);
    }

    [TestMethod]
    public void Apply_WhenNestedFileIsNotATs4Script_ThenHasDepthWarningIsFalse()
    {
        ModFileViewModel viewModel = new(CreateModFile("Scripts/Sub/foo.package"));

        Assert.IsFalse(viewModel.HasDepthWarning);
    }

    // --- Partial On*Changed handlers raise PropertyChanged for derived properties ---

    [TestMethod]
    public void OnStateChanged_WhenStateIsSet_ThenRaisesPropertyChangedForIsEnabled()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", ModFileState.Disabled));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.State = ModFileState.Enabled;

        Assert.Contains(nameof(ModFileViewModel.IsEnabled), changedProperties);
    }

    [TestMethod]
    public void OnSizeBytesChanged_WhenSizeBytesIsSet_ThenRaisesPropertyChangedForSizeText()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package", sizeBytes: 100));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.SizeBytes = 2048;

        Assert.Contains(nameof(ModFileViewModel.SizeText), changedProperties);
    }

    [TestMethod]
    public void OnModifiedUtcChanged_WhenModifiedUtcIsSet_ThenRaisesPropertyChangedForModifiedText()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package"));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.ModifiedUtc = DateTime.UtcNow.AddDays(-1);

        Assert.Contains(nameof(ModFileViewModel.ModifiedText), changedProperties);
    }

    [TestMethod]
    public void OnInstalledUtcChanged_WhenInstalledUtcIsSet_ThenRaisesPropertyChangedForInstalledText()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package"));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.InstalledUtc = DateTime.UtcNow;

        Assert.Contains(nameof(ModFileViewModel.InstalledText), changedProperties);
    }

    [TestMethod]
    public void OnFolderChanged_WhenFolderIsSet_ThenRaisesPropertyChangedForFolderText()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package"));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.Folder = "Extras";

        Assert.Contains(nameof(ModFileViewModel.FolderText), changedProperties);
    }

    // --- Category ---------------------------------------------------------

    [TestMethod]
    public void Apply_WhenFileHasCategory_ThenSetsCategoryProperty()
    {
        ModFile file = CreateModFile("a.package") with { Category = "Scripts" };

        ModFileViewModel viewModel = new(file);

        Assert.AreEqual("Scripts", viewModel.Category);
    }

    [TestMethod]
    public void OnModPageUrlChanged_WhenModPageUrlIsSet_ThenRaisesPropertyChangedForHasModPage()
    {
        ModFileViewModel viewModel = new(CreateModFile("a.package"));
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        viewModel.ModPageUrl = "https://example.com/mod";

        Assert.Contains(nameof(ModFileViewModel.HasModPage), changedProperties);
    }
}
