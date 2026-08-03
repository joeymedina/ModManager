using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class ModsFolderServiceTests
{
    private string originalAppData = string.Empty;
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
        Directory.CreateDirectory(disabledFolderPath);

        originalAppData = Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty;
        Environment.SetEnvironmentVariable("APPDATA", Path.Combine(sandboxPath, "AppData"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("APPDATA", originalAppData);

        if (!string.IsNullOrWhiteSpace(sandboxPath) && Directory.Exists(sandboxPath))
        {
            Directory.Delete(sandboxPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadModsAsync_WhenCalledTwice_ThenUsesStableManifestId()
    {
        CreateFile(modsFolderPath, "WW_main.package");

        var service = new ModsFolderService();

        IReadOnlyList<ManagedMod> firstLoad = await service.LoadModsAsync(modsFolderPath, CancellationToken.None);
        IReadOnlyList<ManagedMod> secondLoad = await service.LoadModsAsync(modsFolderPath, CancellationToken.None);

        Assert.AreEqual(firstLoad.Single().ModId, secondLoad.Single().ModId);
    }

    [TestMethod]
    public async Task DisableModAsync_WhenModIsEnabled_ThenMovesFilesToDisabledFolder()
    {
        CreateFile(modsFolderPath, "MCCC_main.package");

        var service = new ModsFolderService();
        ManagedMod discovered = (await service.LoadModsAsync(modsFolderPath, CancellationToken.None)).Single();

        await service.DisableModAsync(modsFolderPath, discovered.ModId, CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(disabledFolderPath, "MCCC_main.package")));
    }

    [TestMethod]
    public async Task EnableModAsync_WhenModIsDisabled_ThenMovesFilesToEnabledFolder()
    {
        CreateFile(disabledFolderPath, "Basemental_main.package");

        var service = new ModsFolderService();
        ManagedMod discovered = (await service.LoadModsAsync(modsFolderPath, CancellationToken.None)).Single();

        await service.EnableModAsync(modsFolderPath, discovered.ModId, CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Basemental_main.package")));
    }

    [TestMethod]
    public async Task DeleteModAsync_WhenModExists_ThenRemovesModFiles()
    {
        CreateFile(modsFolderPath, "UI_main.package");

        var service = new ModsFolderService();
        ManagedMod discovered = (await service.LoadModsAsync(modsFolderPath, CancellationToken.None)).Single();

        await service.DeleteModAsync(modsFolderPath, discovered.ModId, CancellationToken.None);

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
