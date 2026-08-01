using ModManager.Application.Models;
using ModManager.Application.Services;
using ModManager.Tests.Application.Services;

namespace ModManager.Tests.Application.Services;

[TestClass]
public sealed class ModUpdateOrchestratorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenRequestIsNull_ThenThrowsArgumentNullException()
    {
        var orchestrator = new ModUpdateOrchestrator([]);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => orchestrator.ExecuteAsync(null!));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenModIdIsWhitespace_ThenThrowsArgumentException()
    {
        var orchestrator = new ModUpdateOrchestrator([]);
        var request = new ModUpdateRequest("   ", "mods", false);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => orchestrator.ExecuteAsync(request));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenStrategyIsMissing_ThenThrowsInvalidOperationException()
    {
        var orchestrator = new ModUpdateOrchestrator([]);
        var request = new ModUpdateRequest("wickedwhims", "mods", false);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ExecuteAsync(request));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenMatchingStrategyExists_ThenReturnsStrategyResult()
    {
        var expectedResult = new ModUpdateResult(
            "wickedwhims",
            new ModVersionInfo("187a", "mod.ts4script"),
            new ModReleaseInfo("188a", "January 1st, 2026"),
            -1,
            false,
            0);

        var strategy = new StubModUpdateStrategy("wickedwhims", expectedResult);
        var orchestrator = new ModUpdateOrchestrator([strategy]);
        var request = new ModUpdateRequest("WICKEDWHIMS", "mods", true);

        ModUpdateResult result = await orchestrator.ExecuteAsync(request, CancellationToken.None);

        Assert.AreEqual(expectedResult, result);
    }
}
