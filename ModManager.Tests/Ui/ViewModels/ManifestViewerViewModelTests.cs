using ModManager.Application.Models;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ManifestViewerViewModelTests
{
    [TestMethod]
    public void Constructor_WhenManifestHasFilesGroupsAndInstalls_ThenPopulatesRowsAndResolvesGroupNames()
    {
        ModsManifest manifest = new(
            ModsManifest.CurrentSchemaVersion,
            [new ManifestFileEntry("WW_main.package", "WickedWhims", "group-1", "some notes", "Animations")],
            [new ModGroup("group-1", "WickedWhims Pack", ["WW_main.package"])],
            [new InstallRecord(
                "install-1",
                new InstallSource("adopted", "https://example.com", null),
                "170",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                [new InstallRecordFile("WW_main.package", "abc123", 100)],
                [])]);
        ManifestRawContent raw = new("C:/Mods/.modmanager.json", true, "{ \"schemaVersion\": 1 }");

        ManifestViewerViewModel viewer = new(manifest, raw);

        Assert.AreEqual("C:/Mods/.modmanager.json", viewer.ManifestPath);
        Assert.IsTrue(viewer.ManifestExists);
        Assert.AreEqual(raw.Json, viewer.RawJson);

        Assert.HasCount(1, viewer.Files);
        Assert.AreEqual("WW_main.package", viewer.Files[0].RelativePath);
        Assert.AreEqual("WickedWhims Pack", viewer.Files[0].GroupName);

        Assert.HasCount(1, viewer.Groups);
        Assert.AreEqual("WickedWhims Pack", viewer.Groups[0].Name);

        Assert.HasCount(1, viewer.Installs);
        Assert.AreEqual("adopted", viewer.Installs[0].Provider);
        Assert.AreEqual(1, viewer.Installs[0].FileCount);
    }

    [TestMethod]
    public void Constructor_WhenManifestIsEmptyAndFileIsMissing_ThenShowsEmptyStateAndPlaceholderRawJson()
    {
        ManifestRawContent raw = new("C:/Mods/.modmanager.json", false, null);

        ManifestViewerViewModel viewer = new(ModsManifest.Empty, raw);

        Assert.IsFalse(viewer.ManifestExists);
        Assert.IsFalse(viewer.HasFiles);
        Assert.IsFalse(viewer.HasGroups);
        Assert.IsFalse(viewer.HasInstalls);
        StringAssert.Contains(viewer.RawJson, "doesn't have a manifest file yet");
    }

    [TestMethod]
    public void HasUnsavedChanges_WhenRawJsonUntouched_ThenIsFalse()
    {
        ManifestRawContent raw = new("C:/Mods/.modmanager.json", true, "{ \"schemaVersion\": 1 }");

        ManifestViewerViewModel viewer = new(ModsManifest.Empty, raw);

        Assert.IsFalse(viewer.HasUnsavedChanges);
    }

    [TestMethod]
    public void HasUnsavedChanges_WhenRawJsonIsEdited_ThenIsTrue()
    {
        ManifestRawContent raw = new("C:/Mods/.modmanager.json", true, "{ \"schemaVersion\": 1 }");
        ManifestViewerViewModel viewer = new(ModsManifest.Empty, raw);

        viewer.RawJson = "{ \"schemaVersion\": 1, \"files\": [] }";

        Assert.IsTrue(viewer.HasUnsavedChanges);
    }

    [TestMethod]
    public void HasUnsavedChanges_WhenEditedThenRestoredToOriginal_ThenIsFalseAgain()
    {
        ManifestRawContent raw = new("C:/Mods/.modmanager.json", true, "{ \"schemaVersion\": 1 }");
        ManifestViewerViewModel viewer = new(ModsManifest.Empty, raw);

        viewer.RawJson = "edited";
        viewer.RawJson = raw.Json!;

        Assert.IsFalse(viewer.HasUnsavedChanges);
    }
}
