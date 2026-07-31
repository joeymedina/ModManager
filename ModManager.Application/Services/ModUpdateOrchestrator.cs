using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Application.Services;

public sealed class ModUpdateOrchestrator(IEnumerable<IModUpdateStrategy> strategies) : IModUpdateOrchestrator
{
    private readonly Dictionary<string, IModUpdateStrategy> strategyByModId = strategies
        .ToDictionary(strategy => strategy.ModId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Executes update flow for a requested mod by delegating to the registered strategy.
    /// </summary>
    public Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ModId))
        {
            throw new ArgumentException("A mod identifier is required.", nameof(request));
        }

        if (!strategyByModId.TryGetValue(request.ModId, out IModUpdateStrategy? strategy))
        {
            throw new InvalidOperationException($"No update strategy is registered for mod '{request.ModId}'.");
        }

        return strategy.ExecuteAsync(request, cancellationToken);
    }
}
