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
