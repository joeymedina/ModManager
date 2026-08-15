using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Tests.Application.Services;

internal sealed class InMemoryUpdateCheckStateStore : IUpdateCheckStateStore
{
    public Dictionary<string, UpdateCheckState> Saved { get; private set; } = [];

    public Task<IReadOnlyDictionary<string, UpdateCheckState>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, UpdateCheckState>>(Saved);

    public Task SaveAsync(IReadOnlyDictionary<string, UpdateCheckState> state, CancellationToken cancellationToken = default)
    {
        Saved = new Dictionary<string, UpdateCheckState>(state, StringComparer.Ordinal);
        return Task.CompletedTask;
    }
}
