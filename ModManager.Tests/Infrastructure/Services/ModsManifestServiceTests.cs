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

    [TestMethod]
    public void TryParseRaw_WhenJsonIsValid_ThenReturnsTrueWithTheParsedManifest()
    {
        const string rawJson = """{"SchemaVersion":1,"Files":[{"RelativePath":"a.package","DisplayName":"A"}],"Groups":[],"Installs":[]}""";

        bool success = ModsManifestService.TryParseRaw(rawJson, out ModsManifest? manifest, out string? error);

        Assert.IsTrue(success);
        Assert.IsNull(error);
        Assert.HasCount(1, manifest!.Files);
        Assert.AreEqual("A", manifest.Files[0].DisplayName);
    }

    [TestMethod]
    public void TryParseRaw_WhenJsonIsMalformed_ThenReturnsFalseWithAnError()
    {
        bool success = ModsManifestService.TryParseRaw("{ not valid json", out ModsManifest? manifest, out string? error);

        Assert.IsFalse(success);
        Assert.IsNull(manifest);
        StringAssert.Contains(error, "invalid");
    }

    [TestMethod]
    public void TryParseRaw_WhenSchemaVersionIsOlderThanSupported_ThenReturnsFalseWithAnError()
    {
        const string rawJson = """{"SchemaVersion":0,"Files":[],"Groups":[],"Installs":[]}""";

        bool success = ModsManifestService.TryParseRaw(rawJson, out ModsManifest? manifest, out string? error);

        Assert.IsFalse(success);
        Assert.IsNull(manifest);
        StringAssert.Contains(error, "schema version");
    }
}
