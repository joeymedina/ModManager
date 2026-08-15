using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Infrastructure.Services;
using ModManager.Tests.Application.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class ModsFolderServiceTests
{
    private string sandboxPath = string.Empty;
    private string modsFolderPath = string.Empty;
    private string disabledFolderPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        modsFolderPath = Path.Combine(sandboxPath, "Mods");
        disabledFolderPath = Path.Combine(sandboxPath, "Mods.Disabled");

        Directory.CreateDirectory(modsFolderPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!string.IsNullOrWhiteSpace(sandboxPath) && Directory.Exists(sandboxPath))
        {
            Directory.Delete(sandboxPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadFilesAsync_WhenCalled_ThenWritesNothingAndCreatesNoDisabledFolder()
    {
        CreateFile(modsFolderPath, "WW_main.package");

        var service = new ModsFolderService();

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);

        Assert.HasCount(1, files);
        Assert.IsFalse(Directory.Exists(disabledFolderPath));
    }

    [TestMethod]
    public async Task DisableThenEnableAsync_WhenPathIsNested_ThenRoundTripsPreservingRelativePath()
    {
        const string relativePath = "Sub/Folder/MyMod.package";
        CreateFile(modsFolderPath, relativePath);

        var service = new ModsFolderService();

        IReadOnlyList<ModFileFailure> disableFailures = await service.DisableAsync(modsFolderPath, [relativePath], CancellationToken.None);
        Assert.IsEmpty(disableFailures);
        Assert.IsTrue(File.Exists(Path.Combine(disabledFolderPath, "Sub", "Folder", "MyMod.package")));
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "Sub", "Folder", "MyMod.package")));

        IReadOnlyList<ModFileFailure> enableFailures = await service.EnableAsync(modsFolderPath, [relativePath], CancellationToken.None);
        Assert.IsEmpty(enableFailures);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Sub", "Folder", "MyMod.package")));
    }

    [TestMethod]
    public async Task LoadFilesAsync_WhenSamePathExistsInBothRoots_ThenReturnsOneConflictedRow()
    {
        CreateFile(modsFolderPath, "Dup.package");
        CreateFile(disabledFolderPath, "Dup.package");

        var service = new ModsFolderService();

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);

        ModFile file = files.Single();
        Assert.AreEqual("Dup.package", file.RelativePath);
        Assert.IsTrue(file.IsConflicted);
        Assert.AreEqual(ModFileState.Enabled, file.State);
    }

    [TestMethod]
    public async Task DisableAsync_WhenOnePathIsMissing_ThenAppliesTheRestAndReportsTheFailure()
    {
        CreateFile(modsFolderPath, "Real.package");

        var service = new ModsFolderService();

        IReadOnlyList<ModFileFailure> failures = await service.DisableAsync(
            modsFolderPath,
            ["Real.package", "Missing.package"],
            CancellationToken.None);

        Assert.HasCount(1, failures);
        Assert.AreEqual("Missing.package", failures[0].RelativePath);
        Assert.IsTrue(File.Exists(Path.Combine(disabledFolderPath, "Real.package")));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenFileExists_ThenRemovesFile()
    {
        CreateFile(modsFolderPath, "UI_main.package");

        var service = new ModsFolderService();

        IReadOnlyList<ModFileFailure> failures = await service.DeleteAsync(modsFolderPath, ["UI_main.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "UI_main.package")));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenTheDeletedFileWasAdopted_ThenPrunesItsManifestFileEntryAndInstallRecord()
    {
        CreateFile(modsFolderPath, "Real.package");
        var service = new ModsFolderService();
        await service.AdoptAsync(modsFolderPath, ["Real.package"], "My Mod", null, null, CancellationToken.None);

        IReadOnlyList<ModFileFailure> failures = await service.DeleteAsync(modsFolderPath, ["Real.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
        ModsManifest manifest = await service.LoadManifestAsync(modsFolderPath, CancellationToken.None);
        Assert.IsEmpty(manifest.Files);
        Assert.IsEmpty(manifest.Installs);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenATrackedInstallsOnlyFileIsDeleted_ThenDropsTheInstallAndItsTracking()
    {
        CreateFile(modsFolderPath, "Tracked.package");
        const string manifestJson = """
            {
              "SchemaVersion": 1,
              "Files": [{ "RelativePath": "Tracked.package", "DisplayName": "Tracked Mod" }],
              "Groups": [],
              "Installs": [{
                "InstallId": "install-1",
                "Source": { "Provider": "manual", "ModPageUrl": "https://example.com/mod", "DownloadUrl": null },
                "Version": "1.0",
                "InstalledUtc": "2026-01-01T00:00:00Z",
                "SourceArchivePath": null,
                "Files": [{ "RelativePath": "Tracked.package", "Sha256": "abc", "SizeBytes": 1 }],
                "SkippedEntries": [],
                "Tracking": {
                  "SiteKey": "sacrificial",
                  "SiteModKey": "some-mod",
                  "TrackingUrl": "https://example.com/mod",
                  "BaselineVersion": "1.0",
                  "BaselineUpdatedOnRaw": null,
                  "BaselineCapturedUtc": "2026-01-01T00:00:00Z"
                }
              }]
            }
            """;
        var service = new ModsFolderService();
        await service.SaveManifestRawAsync(modsFolderPath, manifestJson, CancellationToken.None);

        IReadOnlyList<ModFileFailure> failures = await service.DeleteAsync(modsFolderPath, ["Tracked.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
        ModsManifest manifest = await service.LoadManifestAsync(modsFolderPath, CancellationToken.None);
        Assert.IsEmpty(manifest.Installs);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenOnlySomeOfATrackedInstallsFilesAreDeleted_ThenKeepsTheRecordTrimmedAndTracked()
    {
        CreateFile(modsFolderPath, "Keep.package");
        CreateFile(modsFolderPath, "Remove.package");
        const string manifestJson = """
            {
              "SchemaVersion": 1,
              "Files": [],
              "Groups": [],
              "Installs": [{
                "InstallId": "install-1",
                "Source": { "Provider": "manual", "ModPageUrl": "https://example.com/mod", "DownloadUrl": null },
                "Version": "1.0",
                "InstalledUtc": "2026-01-01T00:00:00Z",
                "SourceArchivePath": null,
                "Files": [
                  { "RelativePath": "Keep.package", "Sha256": "abc", "SizeBytes": 1 },
                  { "RelativePath": "Remove.package", "Sha256": "def", "SizeBytes": 1 }
                ],
                "SkippedEntries": [],
                "Tracking": {
                  "SiteKey": "sacrificial",
                  "SiteModKey": "some-mod",
                  "TrackingUrl": "https://example.com/mod",
                  "BaselineVersion": "1.0",
                  "BaselineUpdatedOnRaw": null,
                  "BaselineCapturedUtc": "2026-01-01T00:00:00Z"
                }
              }]
            }
            """;
        var service = new ModsFolderService();
        await service.SaveManifestRawAsync(modsFolderPath, manifestJson, CancellationToken.None);

        IReadOnlyList<ModFileFailure> failures = await service.DeleteAsync(modsFolderPath, ["Remove.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
        ModsManifest manifest = await service.LoadManifestAsync(modsFolderPath, CancellationToken.None);
        InstallRecord record = manifest.Installs.Single();
        Assert.AreEqual("Keep.package", record.Files.Single().RelativePath);
        Assert.IsNotNull(record.Tracking);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenALastRemainingGroupMemberIsDeleted_ThenDropsTheEmptyGroup()
    {
        CreateFile(modsFolderPath, "Grouped.package");
        var service = new ModsFolderService();
        await service.AdoptAsync(modsFolderPath, ["Grouped.package"], "My Mod", null, null, CancellationToken.None);
        await service.AddToGroupAsync(modsFolderPath, ["Grouped.package"], "My Group", CancellationToken.None);

        await service.DeleteAsync(modsFolderPath, ["Grouped.package"], CancellationToken.None);

        ModsManifest manifest = await service.LoadManifestAsync(modsFolderPath, CancellationToken.None);
        Assert.IsEmpty(manifest.Groups);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenFilesExist_ThenRecordsWithoutMovingAnything()
    {
        CreateFile(modsFolderPath, "Loose.package");
        CreateFile(modsFolderPath, "Sub/Nested.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath,
            ["Loose.package", "Sub/Nested.package"],
            "My Old Mod",
            "https://example.com/mod",
            "2.1",
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.HasCount(2, result.Value!.Files);
        Assert.AreEqual("adopted", result.Value.Source.Provider);
        Assert.AreEqual("https://example.com/mod", result.Value.Source.ModPageUrl);
        Assert.IsNull(result.Value.Source.DownloadUrl);
        Assert.AreEqual("2.1", result.Value.Version);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Loose.package")));
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Sub", "Nested.package")));

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsTrue(files.All(file => file.DisplayName == "My Old Mod"));
        Assert.IsTrue(files.All(file => file.InstallId == result.Value.InstallId));
    }

    [TestMethod]
    public async Task AdoptAsync_WhenAPathIsMissing_ThenFailsWithoutAdoptingTheRest()
    {
        CreateFile(modsFolderPath, "Real.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath,
            ["Real.package", "Missing.package"],
            "Partial Mod",
            null,
            null,
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "Missing.package");

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsNull(files.Single().DisplayName);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenModPageUrlHostMatchesARegisteredStrategy_ThenSetsTracking()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var strategy = new StubModSiteStrategy(
            "sacrificialmods.com",
            [],
            resolveModKey: hints => new SiteModKey("ZombieApocalypseDownload"));
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), new ModsManifestService(), new SiteTrackingResolver([strategy]));

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "Zombie Apocalypse", "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "2.3.1", CancellationToken.None);

        Assert.IsTrue(result.Success);
        UpdateTracking? tracking = result.Value!.Tracking;
        Assert.IsNotNull(tracking);
        Assert.AreEqual("sacrificialmods.com", tracking.SiteKey);
        Assert.AreEqual("ZombieApocalypseDownload", tracking.SiteModKey);
        Assert.AreEqual("https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", tracking.TrackingUrl);
        Assert.AreEqual("2.3.1", tracking.BaselineVersion);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenTheStrategyCannotResolveAModKey_ThenTrackingIsStillSetButUnresolved()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var strategy = new StubModSiteStrategy("sacrificialmods.com", [], resolveModKey: _ => null);
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), new ModsManifestService(), new SiteTrackingResolver([strategy]));

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "Zombie Apocalypse", "https://sacrificialmods.com/downloads.html", null, CancellationToken.None);

        Assert.IsNotNull(result.Value!.Tracking);
        Assert.AreEqual("sacrificialmods.com", result.Value.Tracking!.SiteKey);
        Assert.IsNull(result.Value.Tracking.SiteModKey);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenModPageUrlHostMatchesNoRegisteredStrategy_ThenLeavesTrackingNull()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var strategy = new StubModSiteStrategy("sacrificialmods.com", []);
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), new ModsManifestService(), new SiteTrackingResolver([strategy]));

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "Some Other Mod", "https://example.com/mod", null, CancellationToken.None);

        Assert.IsNull(result.Value!.Tracking);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenNoModPageUrlIsGiven_ThenLeavesTrackingNull()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var service = new ModsFolderService();

        ArchiveInstallResult<InstallRecord> result = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "Some Mod", null, null, CancellationToken.None);

        Assert.IsNull(result.Value!.Tracking);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenReAdoptingTheSamePaths_ThenReplacesThePreviousRecordRatherThanDuplicatingIt()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var manifestService = new ModsManifestService();
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), manifestService, new SiteTrackingResolver([]));

        ArchiveInstallResult<InstallRecord> first = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "My Mod", "https://example.com/wrong-page", "1.0", CancellationToken.None);
        ArchiveInstallResult<InstallRecord> second = await service.AdoptAsync(
            modsFolderPath, ["Loose.package"], "My Mod", "https://example.com/correct-page", "1.1", CancellationToken.None);

        Assert.IsTrue(second.Success);
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);
        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);

        Assert.HasCount(1, manifest.Installs);
        Assert.AreEqual(second.Value!.InstallId, manifest.Installs.Single().InstallId);
        Assert.AreEqual("https://example.com/correct-page", manifest.Installs.Single().Source.ModPageUrl);
        Assert.AreNotEqual(first.Value!.InstallId, manifest.Installs.Single().InstallId);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenAFileAlreadyHasACategoryOrGroup_ThenPreservesThemAcrossAdopt()
    {
        CreateFile(modsFolderPath, "Loose.package");
        var manifestService = new ModsManifestService();
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), manifestService, new SiteTrackingResolver([]));

        await service.SetCategoryAsync(modsFolderPath, ["Loose.package"], "Gameplay", CancellationToken.None);
        await service.AddToGroupAsync(modsFolderPath, ["Loose.package"], "My Group", CancellationToken.None);

        await service.AdoptAsync(modsFolderPath, ["Loose.package"], "My Mod", null, null, CancellationToken.None);

        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);
        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        ManifestFileEntry entry = manifest.Files.Single();

        Assert.AreEqual("Gameplay", entry.Category);
        Assert.IsNotNull(entry.GroupId);
        Assert.AreEqual("My Mod", entry.DisplayName);
    }

    [TestMethod]
    public async Task AdoptAsync_WhenReAdoptingASubsetOfAPreviousRecordsPaths_ThenOnlyRemovesTheOverlappingRecord()
    {
        CreateFile(modsFolderPath, "A.package");
        CreateFile(modsFolderPath, "B.package");
        var manifestService = new ModsManifestService();
        var service = new ModsFolderService(new ModsFolderPathService(), new ModsDiscoveryService(), new ModsFileOperationsService(new ModsFolderPathService()), manifestService, new SiteTrackingResolver([]));

        await service.AdoptAsync(modsFolderPath, ["A.package", "B.package"], "Combined Mod", null, "1.0", CancellationToken.None);
        await service.AdoptAsync(modsFolderPath, ["A.package"], "Just A", null, "1.1", CancellationToken.None);

        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);
        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);

        Assert.HasCount(1, manifest.Installs);
        Assert.AreEqual("A.package", manifest.Installs.Single().Files.Single().RelativePath);
        // B.package's own metadata (display name) survives even though its install record is gone —
        // adopting A doesn't touch entries for paths it didn't ask for.
        Assert.AreEqual("Combined Mod", manifest.Files.Single(f => f.RelativePath == "B.package").DisplayName);
    }

    [TestMethod]
    public async Task AddToGroupAsync_WhenGroupIsNew_ThenCreatesItWithSelectedMembers()
    {
        CreateFile(modsFolderPath, "A.package");
        CreateFile(modsFolderPath, "B.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<ModGroup> result = await service.AddToGroupAsync(
            modsFolderPath,
            ["A.package", "B.package"],
            "My Group",
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("My Group", result.Value!.Name);
        Assert.HasCount(2, result.Value.Members);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsTrue(files.All(file => file.GroupId == result.Value.GroupId));
    }

    [TestMethod]
    public async Task AddToGroupAsync_WhenNameMatchesExistingGroup_ThenReusesItInsteadOfCreatingAnother()
    {
        CreateFile(modsFolderPath, "A.package");
        CreateFile(modsFolderPath, "B.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<ModGroup> first = await service.AddToGroupAsync(modsFolderPath, ["A.package"], "Shared", CancellationToken.None);
        ArchiveInstallResult<ModGroup> second = await service.AddToGroupAsync(modsFolderPath, ["B.package"], "shared", CancellationToken.None);

        Assert.AreEqual(first.Value!.GroupId, second.Value!.GroupId);

        IReadOnlyList<ModGroup> groups = await service.LoadGroupsAsync(modsFolderPath, CancellationToken.None);
        ModGroup group = groups.Single();
        Assert.HasCount(2, group.Members);
    }

    [TestMethod]
    public async Task AddToGroupAsync_WhenFileAlreadyBelongsToAnotherGroup_ThenMovesItAndPrunesTheOldGroupIfEmpty()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();

        await service.AddToGroupAsync(modsFolderPath, ["A.package"], "Old Group", CancellationToken.None);
        ArchiveInstallResult<ModGroup> result = await service.AddToGroupAsync(modsFolderPath, ["A.package"], "New Group", CancellationToken.None);

        Assert.IsTrue(result.Success);

        IReadOnlyList<ModGroup> groups = await service.LoadGroupsAsync(modsFolderPath, CancellationToken.None);
        ModGroup group = groups.Single();
        Assert.AreEqual("New Group", group.Name);
    }

    [TestMethod]
    public async Task RemoveFromGroupAsync_WhenLastMemberIsRemoved_ThenPrunesTheEmptyGroup()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();

        await service.AddToGroupAsync(modsFolderPath, ["A.package"], "Solo", CancellationToken.None);
        await service.RemoveFromGroupAsync(modsFolderPath, ["A.package"], CancellationToken.None);

        IReadOnlyList<ModGroup> groups = await service.LoadGroupsAsync(modsFolderPath, CancellationToken.None);
        Assert.IsEmpty(groups);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsNull(files.Single().GroupId);
    }

    [TestMethod]
    public async Task LoadGroupsAsync_WhenAMemberPathNoLongerResolves_ThenStillReturnsItAsAMember()
    {
        CreateFile(modsFolderPath, "A.package");
        CreateFile(modsFolderPath, "B.package");

        var service = new ModsFolderService();
        await service.AddToGroupAsync(modsFolderPath, ["A.package", "B.package"], "Group", CancellationToken.None);

        File.Delete(Path.Combine(modsFolderPath, "B.package"));

        IReadOnlyList<ModGroup> groups = await service.LoadGroupsAsync(modsFolderPath, CancellationToken.None);
        ModGroup group = groups.Single();
        Assert.HasCount(2, group.Members);
        Assert.Contains("B.package", group.Members);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.HasCount(1, files);
    }

    [TestMethod]
    public async Task RemoveFromGroupAsync_WhenEntryHasOnlyACategory_ThenIsNotPruned()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();

        await service.AddToGroupAsync(modsFolderPath, ["A.package"], "Solo", CancellationToken.None);
        await service.SetCategoryAsync(modsFolderPath, ["A.package"], "Scripts", CancellationToken.None);
        await service.RemoveFromGroupAsync(modsFolderPath, ["A.package"], CancellationToken.None);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        ModFile file = files.Single();
        Assert.IsNull(file.GroupId);
        Assert.AreEqual("Scripts", file.Category);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenFileHasNoExistingEntry_ThenCreatesOneWithTheCategory()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<string?> result = await service.SetCategoryAsync(modsFolderPath, ["A.package"], "Scripts", CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Scripts", result.Value);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.AreEqual("Scripts", files.Single().Category);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenCategoryIsBlank_ThenClearsExistingCategory()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();
        await service.SetCategoryAsync(modsFolderPath, ["A.package"], "Scripts", CancellationToken.None);

        ArchiveInstallResult<string?> result = await service.SetCategoryAsync(modsFolderPath, ["A.package"], "   ", CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Value);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsNull(files.Single().Category);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenClearingLeavesNoOtherMetadata_ThenPrunesTheEntry()
    {
        CreateFile(modsFolderPath, "A.package");

        var service = new ModsFolderService();
        await service.SetCategoryAsync(modsFolderPath, ["A.package"], "Scripts", CancellationToken.None);
        await service.SetCategoryAsync(modsFolderPath, ["A.package"], null, CancellationToken.None);

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsNull(files.Single().Category);
        Assert.IsNull(files.Single().DisplayName);
        Assert.IsNull(files.Single().GroupId);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenPathNotFound_ThenFailsAllOrNothing()
    {
        CreateFile(modsFolderPath, "Real.package");

        var service = new ModsFolderService();

        ArchiveInstallResult<string?> result = await service.SetCategoryAsync(
            modsFolderPath,
            ["Real.package", "Missing.package"],
            "Scripts",
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "Missing.package");

        IReadOnlyList<ModFile> files = await service.LoadFilesAsync(modsFolderPath, CancellationToken.None);
        Assert.IsNull(files.Single().Category);
    }

    [TestMethod]
    public async Task ReadManifestRawAsync_WhenNoManifestFileExists_ThenReturnsNotExistsWithNullJson()
    {
        var service = new ModsFolderService();

        ManifestRawContent raw = await service.ReadManifestRawAsync(modsFolderPath, CancellationToken.None);

        Assert.IsFalse(raw.Exists);
        Assert.IsNull(raw.Json);
        Assert.AreEqual(Path.Combine(modsFolderPath, ".modmanager.json"), raw.Path);
    }

    [TestMethod]
    public async Task ReadManifestRawAsync_WhenManifestFileExists_ThenReturnsItsExactText()
    {
        CreateFile(modsFolderPath, "Real.package");
        var service = new ModsFolderService();
        await service.AdoptAsync(modsFolderPath, ["Real.package"], "My Mod", null, null, CancellationToken.None);
        string expectedText = await File.ReadAllTextAsync(Path.Combine(modsFolderPath, ".modmanager.json"));

        ManifestRawContent raw = await service.ReadManifestRawAsync(modsFolderPath, CancellationToken.None);

        Assert.IsTrue(raw.Exists);
        Assert.AreEqual(expectedText, raw.Json);
    }

    [TestMethod]
    public async Task LoadManifestAsync_WhenManifestHasAdoptedFiles_ThenReturnsFilesGroupsAndInstalls()
    {
        CreateFile(modsFolderPath, "Real.package");
        var service = new ModsFolderService();
        await service.AdoptAsync(modsFolderPath, ["Real.package"], "My Mod", "https://example.com", "1.0", CancellationToken.None);
        await service.AddToGroupAsync(modsFolderPath, ["Real.package"], "My Group", CancellationToken.None);

        ModsManifest manifest = await service.LoadManifestAsync(modsFolderPath, CancellationToken.None);

        Assert.HasCount(1, manifest.Files);
        Assert.HasCount(1, manifest.Groups);
        Assert.HasCount(1, manifest.Installs);
        Assert.AreEqual("My Group", manifest.Groups[0].Name);
        Assert.AreEqual("My Mod", manifest.Files[0].DisplayName);
        Assert.AreEqual("adopted", manifest.Installs[0].Source.Provider);
    }

    [TestMethod]
    public async Task SaveManifestRawAsync_WhenJsonIsValid_ThenWritesItAndReturnsTheParsedManifest()
    {
        var service = new ModsFolderService();
        const string rawJson = """{"SchemaVersion":1,"Files":[{"RelativePath":"Edited.package","DisplayName":"Edited"}],"Groups":[],"Installs":[]}""";

        ArchiveInstallResult<ModsManifest> result = await service.SaveManifestRawAsync(modsFolderPath, rawJson, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Value!.Files);
        Assert.AreEqual("Edited", result.Value.Files[0].DisplayName);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, ".modmanager.json")));
    }

    [TestMethod]
    public async Task SaveManifestRawAsync_WhenJsonIsInvalid_ThenFailsAndWritesNothing()
    {
        var service = new ModsFolderService();

        ArchiveInstallResult<ModsManifest> result = await service.SaveManifestRawAsync(modsFolderPath, "{ not valid json", CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "isn't a valid manifest");
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, ".modmanager.json")));
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenCalled_ThenMovesTheFolderAndRewritesRecordPaths()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "SAC_Zombie Apocalypse v2.3.1", category: null, new InstallSource("browser", null, null), version: "2.3.1");
        Assert.IsTrue(install.Success);

        var service = new ModsFolderService();
        ArchiveInstallResult<InstallRecord> renamed = await service.RenameInstallFolderAsync(
            modsFolderPath, install.Value!.InstallId, "Zombie Apocalypse", CancellationToken.None);

        Assert.IsTrue(renamed.Success);
        Assert.IsFalse(Directory.Exists(Path.Combine(modsFolderPath, "SAC_Zombie Apocalypse v2.3.1")));
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Zombie Apocalypse", "loose.package")));
        Assert.AreEqual("Zombie Apocalypse/loose.package", renamed.Value!.Files.Single().RelativePath);
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenCalled_ThenRewritesManifestEntryAndGroupMembership()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "SAC_Zombie Apocalypse v2.3.1", category: "Gameplay", new InstallSource("browser", null, null), version: "2.3.1");

        var service = new ModsFolderService();
        await service.AddToGroupAsync(modsFolderPath, ["SAC_Zombie Apocalypse v2.3.1/loose.package"], "Zombie Stuff", CancellationToken.None);

        await service.RenameInstallFolderAsync(modsFolderPath, install.Value!.InstallId, "Zombie Apocalypse", CancellationToken.None);

        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        ManifestFileEntry entry = manifest.Files.Single();
        Assert.AreEqual("Zombie Apocalypse/loose.package", entry.RelativePath);
        Assert.AreEqual("Gameplay", entry.Category);

        ModGroup group = manifest.Groups.Single();
        Assert.Contains("Zombie Apocalypse/loose.package", group.Members);
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenDesiredNameIsTaken_ThenAppendsANumericSuffixInsteadOfFailing()
    {
        Directory.CreateDirectory(Path.Combine(modsFolderPath, "Zombie Apocalypse"));

        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "SAC_Zombie Apocalypse v2.3.1", category: null, new InstallSource("browser", null, null), version: "2.3.1");

        var service = new ModsFolderService();
        ArchiveInstallResult<InstallRecord> renamed = await service.RenameInstallFolderAsync(
            modsFolderPath, install.Value!.InstallId, "Zombie Apocalypse", CancellationToken.None);

        Assert.IsTrue(renamed.Success);
        Assert.IsTrue(Directory.Exists(Path.Combine(modsFolderPath, "Zombie Apocalypse (2)")));
        Assert.AreEqual("Zombie Apocalypse (2)/loose.package", renamed.Value!.Files.Single().RelativePath);
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenNoInstallMatchesTheId_ThenFails()
    {
        var service = new ModsFolderService();

        ArchiveInstallResult<InstallRecord> result = await service.RenameInstallFolderAsync(
            modsFolderPath, "does-not-exist", "New Name", CancellationToken.None);

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenAlreadyNamedAsRequested_ThenIsANoOp()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "Zombie Apocalypse", category: null, new InstallSource("browser", null, null), version: "2.3.1");

        var service = new ModsFolderService();
        ArchiveInstallResult<InstallRecord> renamed = await service.RenameInstallFolderAsync(
            modsFolderPath, install.Value!.InstallId, "Zombie Apocalypse", CancellationToken.None);

        Assert.IsTrue(renamed.Success);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Zombie Apocalypse", "loose.package")));
    }

    [TestMethod]
    public async Task RenameInstallFolderAsync_WhenModIsDisabled_ThenRenamesUnderTheDisabledRoot()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "SAC_Zombie Apocalypse v2.3.1", category: null, new InstallSource("browser", null, null), version: "2.3.1");

        Directory.CreateDirectory(disabledFolderPath);
        Directory.Move(
            Path.Combine(modsFolderPath, "SAC_Zombie Apocalypse v2.3.1"),
            Path.Combine(disabledFolderPath, "SAC_Zombie Apocalypse v2.3.1"));

        var service = new ModsFolderService();
        ArchiveInstallResult<InstallRecord> renamed = await service.RenameInstallFolderAsync(
            modsFolderPath, install.Value!.InstallId, "Zombie Apocalypse", CancellationToken.None);

        Assert.IsTrue(renamed.Success);
        Assert.IsTrue(File.Exists(Path.Combine(disabledFolderPath, "Zombie Apocalypse", "loose.package")));
        Assert.IsFalse(Directory.Exists(Path.Combine(modsFolderPath, "Zombie Apocalypse")));
    }

    [TestMethod]
    public async Task UpdateInstallTrackingAsync_WhenInstallExists_ThenReplacesItsTracking()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "Zombie Apocalypse", category: null, new InstallSource("browser", null, null), version: null);

        var service = new ModsFolderService();
        UpdateTracking tracking = new("sacrificialmods.com", "ZombieApocalypseDownload", "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "2.3.1", "09-7-2025", DateTime.UtcNow);

        ArchiveInstallResult<InstallRecord> result = await service.UpdateInstallTrackingAsync(modsFolderPath, install.Value!.InstallId, tracking, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(tracking, result.Value!.Tracking);

        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        Assert.AreEqual(tracking, manifest.Installs.Single().Tracking);
    }

    [TestMethod]
    public async Task UpdateInstallTrackingAsync_WhenCalledTwice_ThenSecondCallReplacesTheFirstRatherThanDuplicatingTheRecord()
    {
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()), new SiteTrackingResolver([]));
        var layout = new ModsFolderLayout(modsFolderPath, disabledFolderPath);

        string barePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(barePath, "a");
        ArchiveInstallResult<InstallRecord> install = await archiveService.InstallAsync(
            barePath, new HashSet<string>(), layout, "Zombie Apocalypse", category: null, new InstallSource("browser", null, null), version: null);

        var service = new ModsFolderService();
        UpdateTracking first = new("sacrificialmods.com", "ZombieApocalypseDownload", "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload", "2.3.1", null, DateTime.UtcNow);
        UpdateTracking second = first with { BaselineVersion = "2.3.2" };

        await service.UpdateInstallTrackingAsync(modsFolderPath, install.Value!.InstallId, first, CancellationToken.None);
        await service.UpdateInstallTrackingAsync(modsFolderPath, install.Value!.InstallId, second, CancellationToken.None);

        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        Assert.HasCount(1, manifest.Installs);
        Assert.AreEqual("2.3.2", manifest.Installs.Single().Tracking!.BaselineVersion);
    }

    [TestMethod]
    public async Task UpdateInstallTrackingAsync_WhenNoInstallMatchesTheId_ThenFails()
    {
        var service = new ModsFolderService();
        UpdateTracking tracking = new("sacrificialmods.com", "key", "https://sacrificialmods.com/downloads.html#key", null, null, DateTime.UtcNow);

        ArchiveInstallResult<InstallRecord> result = await service.UpdateInstallTrackingAsync(modsFolderPath, "does-not-exist", tracking, CancellationToken.None);

        Assert.IsFalse(result.Success);
    }

    private static void CreateFile(string root, string relativePath)
    {
        string fullPath = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, "mod");
    }
}
