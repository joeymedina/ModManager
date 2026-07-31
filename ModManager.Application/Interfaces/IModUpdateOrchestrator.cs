using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

public interface IModUpdateOrchestrator
{
    /// <summary>
    /// Executes update flow for the specified mod.
    /// </summary>
    Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default);
}
