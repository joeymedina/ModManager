using System.IO.Compression;
using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class ArchiveInstallServiceTests
{
    private string sandboxPath = string.Empty;
    private string modsFolderPath = string.Empty;
    private ModsFolderLayout layout = null!;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
        modsFolderPath = Path.Combine(sandboxPath, "Mods");
        Directory.CreateDirectory(modsFolderPath);
        layout = new ModsFolderLayout(modsFolderPath, Path.Combine(sandboxPath, "Mods.Disabled"));
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
    public async Task PreviewAsync_WhenArchiveHasMixedEntries_ThenClassifiesEachOne()
    {
        string archivePath = CreateZip(("Main.package", "a"), ("Optional/Bright.package", "b"), ("readme.txt", "c"));
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<ArchivePreview> result = await service.PreviewAsync(archivePath);

        Assert.IsTrue(result.Success);
        ArchiveEntryPreview main = result.Value!.Entries.Single(entry => entry.EntryName == "Main.package");
        Assert.AreEqual(ArchiveEntryKind.Installable, main.Kind);
        Assert.IsTrue(main.SelectedByDefault);

        ArchiveEntryPreview variant = result.Value.Entries.Single(entry => entry.EntryName == "Optional/Bright.package");
        Assert.AreEqual(ArchiveEntryKind.Variant, variant.Kind);
        Assert.IsFalse(variant.SelectedByDefault);

        ArchiveEntryPreview readme = result.Value.Entries.Single(entry => entry.EntryName == "readme.txt");
        Assert.AreEqual(ArchiveEntryKind.NotInstallable, readme.Kind);
        Assert.IsFalse(readme.SelectedByDefault);
    }

    [TestMethod]
    public async Task PreviewAsync_WhenPackagesShareAStem_ThenBothAreFlaggedAsVariants()
    {
        string archivePath = CreateZip(("Eyes_Blue.package", "a"), ("Eyes_Green.package", "b"));
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<ArchivePreview> result = await service.PreviewAsync(archivePath);

        Assert.IsTrue(result.Value!.Entries.All(entry => entry.Kind == ArchiveEntryKind.Variant));
    }

    [TestMethod]
    public async Task PreviewAsync_WhenExtensionIsNotZip_ThenFails()
    {
        string archivePath = Path.Combine(sandboxPath, "Mod.rar");
        File.WriteAllText(archivePath, "not a zip");
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<ArchivePreview> result = await service.PreviewAsync(archivePath);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "extract manually");
    }

    [TestMethod]
    public async Task InstallAsync_WhenEntriesAreSelected_ThenWritesOnlySelectedEntries()
    {
        string archivePath = CreateZip(("Main.package", "a"), ("Optional/Bright.package", "b"), ("readme.txt", "c"));
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<InstallRecord> result = await service.InstallAsync(
            archivePath,
            selectedEntryNames: new HashSet<string> { "Main.package" },
            layout,
            "My Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: null);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Value!.Files);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Main.package")));
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Optional", "Bright.package")));
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "My Mod", "readme.txt")));
        Assert.Contains("readme.txt", result.Value.SkippedEntries);
    }

    [TestMethod]
    public async Task InstallAsync_WhenTs4ScriptIsNested_ThenFlattensToModFolderRoot()
    {
        string archivePath = CreateZip(("Scripts/Sub/mod.ts4script", "a"));
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<InstallRecord> result = await service.InstallAsync(
            archivePath,
            selectedEntryNames: new HashSet<string> { "Scripts/Sub/mod.ts4script" },
            layout,
            "Script Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: null);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Script Mod", "mod.ts4script")));
        Assert.AreEqual("Script Mod/mod.ts4script", result.Value!.Files.Single().RelativePath);
    }

    [TestMethod]
    public async Task InstallAsync_WhenFolderNameCollides_ThenDedupesWithNumericSuffix()
    {
        Directory.CreateDirectory(Path.Combine(modsFolderPath, "My Mod"));
        string archivePath = CreateZip(("Main.package", "a"));
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<InstallRecord> result = await service.InstallAsync(
            archivePath,
            selectedEntryNames: new HashSet<string> { "Main.package" },
            layout,
            "My Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: null);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "My Mod (2)", "Main.package")));
    }

    [TestMethod]
    public async Task InstallAsync_WhenCalled_ThenRecordsInstallInManifestForLaterLoad()
    {
        string archivePath = CreateZip(("Main.package", "a"));
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService);

        ArchiveInstallResult<InstallRecord> installResult = await archiveService.InstallAsync(
            archivePath,
            selectedEntryNames: new HashSet<string> { "Main.package" },
            layout,
            "My Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: "1.0");

        Assert.IsTrue(installResult.Success);

        var folderService = new ModsFolderService();
        IReadOnlyList<ModFile> files = await folderService.LoadFilesAsync(modsFolderPath, CancellationToken.None);

        ModFile installed = files.Single();
        Assert.AreEqual("My Mod", installed.DisplayName);
        Assert.AreEqual(installResult.Value!.InstallId, installed.InstallId);
    }

    [TestMethod]
    public async Task InstallAsync_WhenCategoryIsGiven_ThenRecordsItInManifestForLaterLoad()
    {
        string archivePath = CreateZip(("Main.package", "a"));
        var manifestService = new ModsManifestService();
        var archiveService = new ArchiveInstallService(manifestService);

        ArchiveInstallResult<InstallRecord> installResult = await archiveService.InstallAsync(
            archivePath,
            selectedEntryNames: new HashSet<string> { "Main.package" },
            layout,
            "My Mod",
            category: "Scripts",
            new InstallSource("manual", null, null),
            version: null);

        Assert.IsTrue(installResult.Success);

        var folderService = new ModsFolderService();
        IReadOnlyList<ModFile> files = await folderService.LoadFilesAsync(modsFolderPath, CancellationToken.None);

        Assert.AreEqual("Scripts", files.Single().Category);
    }

    [TestMethod]
    public async Task InstallAsync_WhenBareModFile_ThenInstallsAsSingleFile()
    {
        string bareFilePath = Path.Combine(sandboxPath, "loose.package");
        File.WriteAllText(bareFilePath, "a");
        var service = new ArchiveInstallService(new ModsManifestService());

        ArchiveInstallResult<InstallRecord> result = await service.InstallAsync(
            bareFilePath,
            selectedEntryNames: new HashSet<string>(),
            layout,
            "Loose Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: null);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "Loose Mod", "loose.package")));
    }

    private string CreateZip(params (string EntryName, string Content)[] entries)
    {
        string archivePath = Path.Combine(sandboxPath, $"{Guid.NewGuid():N}.zip");
        using FileStream stream = File.Create(archivePath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);

        foreach ((string entryName, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }

        return archivePath;
    }
}
