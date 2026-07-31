using ModManager.Application.Models;

namespace ModManager.Application.Interfaces;

public interface IModUpdateStrategy
{
    string ModId { get; }

    /// <summary>
    /// Executes a mod-specific version check and optional download.
    /// </summary>
    Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default);
}
