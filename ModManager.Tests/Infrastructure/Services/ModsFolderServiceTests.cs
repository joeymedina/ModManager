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
