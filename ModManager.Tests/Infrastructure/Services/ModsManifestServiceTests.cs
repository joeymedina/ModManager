using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class ModsManifestServiceTests
{
    private string sandboxPath = string.Empty;
    private ModsFolderLayout layout = null!;
    private string manifestPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        string modsFolderPath = Path.Combine(sandboxPath, "Mods");
        Directory.CreateDirectory(modsFolderPath);

        layout = new ModsFolderLayout(modsFolderPath, Path.Combine(sandboxPath, "Mods.Disabled"));
        manifestPath = Path.Combine(modsFolderPath, ".modmanager.json");
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
    public async Task LoadAsync_WhenManifestIsCorrupt_ThenBacksItUpAndRefusesToSaveOverIt()
    {
        const string original = "{ this is not valid json";
        await File.WriteAllTextAsync(manifestPath, original);

        var service = new ModsManifestService();
        ModsManifest manifest = await service.LoadAsync(layout, CancellationToken.None);

        Assert.IsNotNull(manifest.UnreadableReason);

        string[] backups = Directory.GetFiles(layout.ModsFolderPath, ".modmanager.json.corrupt-*");
        Assert.HasCount(1, backups);
        Assert.AreEqual(original, await File.ReadAllTextAsync(backups[0]));

        // The whole point of the flag: a later save must not replace the real history with nothing.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.SaveAsync(layout, manifest with { Files = [new ManifestFileEntry("a.package", "A")] }, CancellationToken.None));

        Assert.AreEqual(original, await File.ReadAllTextAsync(manifestPath));
    }

    [TestMethod]
    public async Task SaveAsync_WhenManifestLoadedCleanly_ThenRoundTrips()
    {
        var service = new ModsManifestService();

        ModsManifest saved = ModsManifest.Empty with { Files = [new ManifestFileEntry("a.package", "A")] };
        await service.SaveAsync(layout, saved, CancellationToken.None);

        ModsManifest loaded = await service.LoadAsync(layout, CancellationToken.None);

        Assert.IsNull(loaded.UnreadableReason);
        Assert.HasCount(1, loaded.Files);
        Assert.AreEqual("A", loaded.Files[0].DisplayName);
    }
}
