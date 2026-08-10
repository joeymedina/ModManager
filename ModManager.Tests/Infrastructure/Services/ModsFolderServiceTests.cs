using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

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
