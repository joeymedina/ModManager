using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class ModsFileOperationsServiceTests
{
    private string sandboxPath = string.Empty;
    private string installRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        installRoot = Path.Combine(sandboxPath, "Mods", "SomeMod");
        Directory.CreateDirectory(installRoot);
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
    public async Task DeleteStalePathsAsync_WhenFileExists_ThenDeletesIt()
    {
        string filePath = Path.Combine(installRoot, "Old.package");
        File.WriteAllText(filePath, "stale");
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        IReadOnlyList<ModFileFailure> failures = await service.DeleteStalePathsAsync(installRoot, ["Old.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
        Assert.IsFalse(File.Exists(filePath));
    }

    [TestMethod]
    public async Task DeleteStalePathsAsync_WhenFileAlreadyGone_ThenTreatsItAsCleanNotAFailure()
    {
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        IReadOnlyList<ModFileFailure> failures = await service.DeleteStalePathsAsync(installRoot, ["NeverExisted.package"], CancellationToken.None);

        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task DeleteStalePathsAsync_WhenDeletionEmptiesADirectory_ThenRemovesTheDirectoryUpToTheRoot()
    {
        string nestedFile = Path.Combine(installRoot, "Assets", "Sub", "Old.package");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedFile)!);
        File.WriteAllText(nestedFile, "stale");
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        await service.DeleteStalePathsAsync(installRoot, ["Assets/Sub/Old.package"], CancellationToken.None);

        Assert.IsFalse(Directory.Exists(Path.Combine(installRoot, "Assets", "Sub")));
        Assert.IsFalse(Directory.Exists(Path.Combine(installRoot, "Assets")));
        Assert.IsTrue(Directory.Exists(installRoot), "Should stop removing at installRoot itself.");
    }

    [TestMethod]
    public async Task DeleteStalePathsAsync_WhenDirectoryStillHasOtherFiles_ThenLeavesItInPlace()
    {
        string staleFile = Path.Combine(installRoot, "Assets", "Old.package");
        string keptFile = Path.Combine(installRoot, "Assets", "Keep.package");
        Directory.CreateDirectory(Path.GetDirectoryName(staleFile)!);
        File.WriteAllText(staleFile, "stale");
        File.WriteAllText(keptFile, "keep");
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        await service.DeleteStalePathsAsync(installRoot, ["Assets/Old.package"], CancellationToken.None);

        Assert.IsTrue(Directory.Exists(Path.Combine(installRoot, "Assets")));
        Assert.IsTrue(File.Exists(keptFile));
    }

    [TestMethod]
    public async Task DeleteStalePathsAsync_WhenPathEscapesInstallRoot_ThenSkipsItAndReportsAFailure()
    {
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        IReadOnlyList<ModFileFailure> failures = await service.DeleteStalePathsAsync(installRoot, ["../../Escape.package"], CancellationToken.None);

        Assert.HasCount(1, failures);
        Assert.AreEqual("../../Escape.package", failures[0].RelativePath);
    }

    [TestMethod]
    public async Task DeleteStalePathsAsync_WhenOneEntryEscapesAndAnotherIsValid_ThenStillDeletesTheValidOne()
    {
        string validFile = Path.Combine(installRoot, "Keep.package");
        File.WriteAllText(validFile, "data");
        var service = new ModsFileOperationsService(new ModsFolderPathService());

        IReadOnlyList<ModFileFailure> failures = await service.DeleteStalePathsAsync(
            installRoot,
            ["../../Escape.package", "Old.package"],
            CancellationToken.None);

        Assert.HasCount(1, failures);
        Assert.AreEqual("../../Escape.package", failures[0].RelativePath);
    }
}
