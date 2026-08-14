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
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

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
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

        ArchiveInstallResult<ArchivePreview> result = await service.PreviewAsync(archivePath);

        Assert.IsTrue(result.Value!.Entries.All(entry => entry.Kind == ArchiveEntryKind.Variant));
    }

    [TestMethod]
    public async Task PreviewAsync_WhenExtensionIsNotZip_ThenFails()
    {
        string archivePath = Path.Combine(sandboxPath, "Mod.rar");
        File.WriteAllText(archivePath, "not a zip");
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

        ArchiveInstallResult<ArchivePreview> result = await service.PreviewAsync(archivePath);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "extract manually");
    }

    [TestMethod]
    public async Task InstallAsync_WhenEntriesAreSelected_ThenWritesOnlySelectedEntries()
    {
        string archivePath = CreateZip(("Main.package", "a"), ("Optional/Bright.package", "b"), ("readme.txt", "c"));
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

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
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

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
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

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
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

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
        var archiveService = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

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
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

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

    [TestMethod]
    public async Task InstallAsync_WhenSuperseding_ThenReplacesTheRecordInsteadOfAppending()
    {
        var manifestService = new ModsManifestService();
        var service = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "v1"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "1.0");
        Assert.IsTrue(first.Success);

        string secondArchive = CreateZip(("Main.package", "v2"));
        ArchiveInstallResult<InstallRecord> second = await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "2.0", supersedes: first.Value);
        Assert.IsTrue(second.Success);

        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        Assert.HasCount(1, manifest.Installs);
        Assert.AreEqual(second.Value!.InstallId, manifest.Installs[0].InstallId);
        Assert.AreEqual("v2", File.ReadAllText(Path.Combine(modsFolderPath, "My Mod", "Main.package")));
    }

    [TestMethod]
    public async Task InstallAsync_WhenSuperseding_ThenExtractsIntoTheExistingFolderRatherThanADedupedOne()
    {
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: null);

        string secondArchive = CreateZip(("Main.package", "b"));
        await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: null, supersedes: first.Value);

        Assert.IsFalse(Directory.Exists(Path.Combine(modsFolderPath, "My Mod (2)")));
        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Main.package")));
    }

    [TestMethod]
    public async Task InstallAsync_WhenSuperseding_ThenDeletesFilesTheNewVersionDropped()
    {
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"), ("Old.package", "b"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive,
            new HashSet<string> { "Main.package", "Old.package" },
            layout,
            "My Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: "1.0");

        string secondArchive = CreateZip(("Main.package", "a2"));
        await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "2.0", supersedes: first.Value);

        Assert.IsTrue(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Main.package")));
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Old.package")));
    }

    [TestMethod]
    public async Task InstallAsync_WhenSuperseding_ThenDropsManifestEntriesForDeletedPaths()
    {
        var manifestService = new ModsManifestService();
        var service = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"), ("Old.package", "b"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive,
            new HashSet<string> { "Main.package", "Old.package" },
            layout,
            "My Mod",
            category: null,
            new InstallSource("manual", null, null),
            version: "1.0");

        string secondArchive = CreateZip(("Main.package", "a2"));
        await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "2.0", supersedes: first.Value);

        ModsManifest manifest = await manifestService.LoadAsync(layout, CancellationToken.None);
        Assert.IsFalse(manifest.Files.Any(entry => entry.RelativePath == "My Mod/Old.package"));
        Assert.IsTrue(manifest.Files.Any(entry => entry.RelativePath == "My Mod/Main.package"));
    }

    [TestMethod]
    public async Task InstallAsync_WhenSupersedingAPathThatKeepsGroupMembership_ThenCarriesTheGroupIdForward()
    {
        var manifestService = new ModsManifestService();
        var service = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "1.0");

        ModsManifest manifestBeforeSupersede = await manifestService.LoadAsync(layout, CancellationToken.None);
        ManifestFileEntry entry = manifestBeforeSupersede.Files.Single(entry => entry.RelativePath == "My Mod/Main.package");
        await manifestService.SaveAsync(
            layout,
            manifestBeforeSupersede with { Files = [entry with { GroupId = "group-1", Notes = "keep me" }] },
            CancellationToken.None);

        string secondArchive = CreateZip(("Main.package", "a2"));
        await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "2.0", supersedes: first.Value);

        ModsManifest manifestAfterSupersede = await manifestService.LoadAsync(layout, CancellationToken.None);
        ManifestFileEntry updatedEntry = manifestAfterSupersede.Files.Single(entry => entry.RelativePath == "My Mod/Main.package");
        Assert.AreEqual("group-1", updatedEntry.GroupId);
        Assert.AreEqual("keep me", updatedEntry.Notes);
    }

    [TestMethod]
    public async Task InstallAsync_WhenSupersedingAModThatIsCurrentlyDisabled_ThenExtractsIntoTheDisabledRoot()
    {
        var service = new ArchiveInstallService(new ModsManifestService(), new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"));
        ArchiveInstallResult<InstallRecord> first = await service.InstallAsync(
            firstArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "1.0");

        Directory.CreateDirectory(layout.DisabledModsFolderPath);
        Directory.Move(
            Path.Combine(modsFolderPath, "My Mod"),
            Path.Combine(layout.DisabledModsFolderPath, "My Mod"));

        string secondArchive = CreateZip(("Main.package", "a2"));
        ArchiveInstallResult<InstallRecord> second = await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: "2.0", supersedes: first.Value);

        Assert.IsTrue(second.Success);
        Assert.IsTrue(File.Exists(Path.Combine(layout.DisabledModsFolderPath, "My Mod", "Main.package")));
        Assert.IsFalse(File.Exists(Path.Combine(modsFolderPath, "My Mod", "Main.package")));
        Assert.AreEqual("My Mod/Main.package", second.Value!.Files.Single().RelativePath);
    }

    [TestMethod]
    public async Task InstallAsync_WhenReinstallingOverAnExistingPathWithoutSupersede_ThenPreservesGroupMembership()
    {
        var manifestService = new ModsManifestService();
        var service = new ArchiveInstallService(manifestService, new ModsFileOperationsService(new ModsFolderPathService()));

        string firstArchive = CreateZip(("Main.package", "a"));
        await service.InstallAsync(
            firstArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: null);

        ModsManifest manifestBeforeReinstall = await manifestService.LoadAsync(layout, CancellationToken.None);
        ManifestFileEntry entry = manifestBeforeReinstall.Files.Single(entry => entry.RelativePath == "My Mod/Main.package");
        await manifestService.SaveAsync(
            layout,
            manifestBeforeReinstall with { Files = [entry with { GroupId = "group-1" }] },
            CancellationToken.None);

        Directory.Delete(Path.Combine(modsFolderPath, "My Mod"), recursive: true);
        string secondArchive = CreateZip(("Main.package", "a"));
        await service.InstallAsync(
            secondArchive, new HashSet<string> { "Main.package" }, layout, "My Mod", category: null, new InstallSource("manual", null, null), version: null);

        ModsManifest manifestAfterReinstall = await manifestService.LoadAsync(layout, CancellationToken.None);
        Assert.AreEqual("group-1", manifestAfterReinstall.Files.Single(entry => entry.RelativePath == "My Mod/Main.package").GroupId);
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
