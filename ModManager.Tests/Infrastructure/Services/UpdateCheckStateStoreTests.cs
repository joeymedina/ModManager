using ModManager.Application.Models;
using ModManager.Infrastructure.Services;

namespace ModManager.Tests.Infrastructure.Services;

[TestClass]
[DoNotParallelize]
public sealed class UpdateCheckStateStoreTests
{
    private string sandboxPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        sandboxPath = Path.Combine(Path.GetTempPath(), "ModManager.Tests", Guid.NewGuid().ToString("N"));
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
    public async Task LoadAsync_WhenNoFileExists_ThenReturnsEmpty()
    {
        var store = new UpdateCheckStateStore(sandboxPath);

        IReadOnlyDictionary<string, UpdateCheckState> state = await store.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(state);
    }

    [TestMethod]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsState()
    {
        var store = new UpdateCheckStateStore(sandboxPath);
        UpdateCheckState entry = new("install-1", SiteUpdateStatus.UpdateAvailable, "2.3.2", "10-12-2025", null, DateTime.UtcNow);

        await store.SaveAsync(new Dictionary<string, UpdateCheckState> { ["install-1"] = entry }, CancellationToken.None);
        IReadOnlyDictionary<string, UpdateCheckState> loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(entry, loaded["install-1"]);
    }

    [TestMethod]
    public async Task SaveAsync_WhenDirectoryDoesNotExist_ThenCreatesIt()
    {
        var store = new UpdateCheckStateStore(sandboxPath);

        await store.SaveAsync(new Dictionary<string, UpdateCheckState>(), CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(sandboxPath, "update-check-state.json")));
    }

    [TestMethod]
    public async Task LoadAsync_WhenFileIsNotValidJson_ThenReturnsEmptyRatherThanThrowing()
    {
        Directory.CreateDirectory(sandboxPath);
        await File.WriteAllTextAsync(Path.Combine(sandboxPath, "update-check-state.json"), "{ not valid json");
        var store = new UpdateCheckStateStore(sandboxPath);

        IReadOnlyDictionary<string, UpdateCheckState> state = await store.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(state);
    }

    [TestMethod]
    public async Task SaveAsync_WhenCalledTwice_ThenSecondSaveOverwritesTheFirst()
    {
        var store = new UpdateCheckStateStore(sandboxPath);
        DateTime checkedUtc = DateTime.UtcNow;

        await store.SaveAsync(new Dictionary<string, UpdateCheckState>
        {
            ["install-1"] = new UpdateCheckState("install-1", SiteUpdateStatus.UpToDate, "1.0", null, null, checkedUtc)
        }, CancellationToken.None);

        await store.SaveAsync(new Dictionary<string, UpdateCheckState>
        {
            ["install-1"] = new UpdateCheckState("install-1", SiteUpdateStatus.UpdateAvailable, "1.1", null, null, checkedUtc)
        }, CancellationToken.None);

        IReadOnlyDictionary<string, UpdateCheckState> loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(SiteUpdateStatus.UpdateAvailable, loaded["install-1"].LastStatus);
    }
}
