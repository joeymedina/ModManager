using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

/// <summary>
/// Persists volatile per-record check state — see <see cref="UpdateCheckState"/> for why this is kept
/// separate from the Mods-folder manifest.
/// </summary>
public interface IUpdateCheckStateStore
{
    Task<IReadOnlyDictionary<string, UpdateCheckState>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyDictionary<string, UpdateCheckState> state, CancellationToken cancellationToken = default);
}
