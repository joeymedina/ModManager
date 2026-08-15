using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Tests.Application.Services;

internal sealed class StubModSiteStrategy(
    string siteKey,
    IReadOnlyList<SiteObservation> observations,
    Func<ModKeyHints, SiteModKey?>? resolveModKey = null,
    Exception? throwOnFetch = null,
    bool hangUntilCanceled = false) : IModSiteStrategy
{
    public string SiteKey { get; } = siteKey;

    public IReadOnlyList<string> Hosts { get; } = [siteKey];

    public SiteCapabilities Capabilities { get; } = new();

    public List<IReadOnlyList<SiteModKey>> FetchCalls { get; } = [];

    public SiteModKey? TryResolveModKey(ModKeyHints hints) => resolveModKey?.Invoke(hints);

    public async Task<IReadOnlyList<SiteObservation>> FetchObservationsAsync(IReadOnlyList<SiteModKey> modKeys, CancellationToken cancellationToken = default)
    {
        FetchCalls.Add(modKeys);

        if (hangUntilCanceled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (throwOnFetch is not null)
        {
            throw throwOnFetch;
        }

        return observations;
    }
}
