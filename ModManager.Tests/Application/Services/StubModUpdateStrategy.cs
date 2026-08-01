using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Tests.Application.Services;

internal sealed class StubModUpdateStrategy(string modId, ModUpdateResult result) : IModUpdateStrategy
{
    public string ModId { get; } = modId;

    public Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(result);
    }
}
