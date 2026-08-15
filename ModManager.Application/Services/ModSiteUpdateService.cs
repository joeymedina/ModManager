using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Application.Services;

/// <summary>
/// Base orchestrator for site-based update checking. Owns everything a site strategy must not:
/// routing by site, resolving still-unknown mod keys, joining fetched observations back to tracked
/// installs, version normalization and comparison precedence, the three-state outcome, per-strategy
/// error containment, and check-state persistence. See <see cref="IModSiteStrategy"/> for why that
/// split exists.
/// </summary>
public sealed class ModSiteUpdateService(
    IEnumerable<IModSiteStrategy> strategies,
    IUpdateCheckStateStore stateStore,
    TimeSpan? strategyTimeout = null) : IModSiteUpdateService
{
    private static readonly TimeSpan DefaultStrategyTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, IModSiteStrategy> strategyBySiteKey = strategies
        .ToDictionary(strategy => strategy.SiteKey, StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan effectiveStrategyTimeout = strategyTimeout ?? DefaultStrategyTimeout;

    public async Task<IReadOnlyList<SiteUpdateCheckResult>> CheckAsync(IReadOnlyList<TrackedMod> trackedMods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackedMods);

        DateTime checkedUtc = DateTime.UtcNow;

        IReadOnlyList<SiteUpdateCheckResult>[] perSiteResults = await Task.WhenAll(trackedMods
            .Where(mod => mod.Record.Tracking is not null)
            .GroupBy(mod => mod.Record.Tracking!.SiteKey, StringComparer.OrdinalIgnoreCase)
            .Select(siteGroup => CheckSiteAsync(siteGroup.Key, [.. siteGroup], checkedUtc, cancellationToken)));

        List<SiteUpdateCheckResult> results = [.. perSiteResults.SelectMany(siteResults => siteResults)];

        await PersistCheckStateAsync(results, checkedUtc, cancellationToken);
        return results;
    }

    private async Task<IReadOnlyList<SiteUpdateCheckResult>> CheckSiteAsync(
        string siteKey,
        IReadOnlyList<TrackedMod> mods,
        DateTime checkedUtc,
        CancellationToken cancellationToken)
    {
        if (!strategyBySiteKey.TryGetValue(siteKey, out IModSiteStrategy? strategy))
        {
            return [.. mods.Select(mod => Indeterminate(mod.Record, $"No update strategy is registered for site \"{siteKey}\".", checkedUtc))];
        }

        (Dictionary<string, SiteModKey> resolvedKeyByInstallId, List<SiteUpdateCheckResult> unresolvedResults) = ResolveKeys(strategy, mods, checkedUtc);

        List<TrackedMod> resolvedMods = [.. mods.Where(mod => resolvedKeyByInstallId.ContainsKey(mod.Record.InstallId))];
        if (resolvedMods.Count == 0)
        {
            return unresolvedResults;
        }

        IReadOnlyList<SiteModKey> keysToFetch = [.. resolvedKeyByInstallId.Values.DistinctBy(key => key.Value, StringComparer.OrdinalIgnoreCase)];

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveStrategyTimeout);

        IReadOnlyList<SiteObservation> observations;
        try
        {
            observations = await strategy.FetchObservationsAsync(keysToFetch, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's cancellation — that propagates normally instead.
            unresolvedResults.AddRange(resolvedMods.Select(mod => Indeterminate(mod.Record, $"{siteKey} did not respond in time.", checkedUtc)));
            return unresolvedResults;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unresolvedResults.AddRange(resolvedMods.Select(mod => Indeterminate(mod.Record, $"Could not check {siteKey}: {ex.Message}", checkedUtc)));
            return unresolvedResults;
        }

        Dictionary<string, SiteObservation> observationByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (SiteObservation observation in observations)
        {
            observationByKey.TryAdd(observation.ModKey.Value, observation);
        }

        List<SiteUpdateCheckResult> results = unresolvedResults;
        foreach (TrackedMod mod in resolvedMods)
        {
            SiteModKey resolvedKey = resolvedKeyByInstallId[mod.Record.InstallId];
            SiteModKey? newlyResolvedKey = mod.Record.Tracking!.SiteModKey is null ? resolvedKey : null;

            results.Add(observationByKey.TryGetValue(resolvedKey.Value, out SiteObservation? observation)
                ? Compare(mod.Record, observation, checkedUtc, newlyResolvedKey)
                : Indeterminate(mod.Record, "Not found on the site.", checkedUtc, newlyResolvedKey));
        }

        return results;
    }

    /// <summary>
    /// Resolves a key for every mod that doesn't already have one recorded, so one newly successful
    /// resolution can join the same fetch batch instead of waiting for a second check.
    /// </summary>
    private static (Dictionary<string, SiteModKey> ResolvedKeyByInstallId, List<SiteUpdateCheckResult> UnresolvedResults) ResolveKeys(
        IModSiteStrategy strategy,
        IReadOnlyList<TrackedMod> mods,
        DateTime checkedUtc)
    {
        Dictionary<string, SiteModKey> resolvedKeyByInstallId = [];
        List<SiteUpdateCheckResult> unresolvedResults = [];

        foreach (TrackedMod mod in mods)
        {
            UpdateTracking tracking = mod.Record.Tracking!;
            if (tracking.SiteModKey is { } existingKey)
            {
                resolvedKeyByInstallId[mod.Record.InstallId] = new SiteModKey(existingKey);
                continue;
            }

            ModKeyHints hints = new(
                tracking.TrackingUrl,
                mod.Record.Source.DownloadUrl,
                mod.DisplayName,
                [.. mod.Record.Files.Select(file => file.RelativePath)]);

            SiteModKey? resolved = strategy.TryResolveModKey(hints);
            if (resolved is not null)
            {
                resolvedKeyByInstallId[mod.Record.InstallId] = resolved;
            }
            else
            {
                unresolvedResults.Add(Indeterminate(mod.Record, "Could not determine which mod on this site this is — link a mod page.", checkedUtc));
            }
        }

        return (resolvedKeyByInstallId, unresolvedResults);
    }

    /// <summary>
    /// Version takes precedence when both sides have one — it's the less noisy signal. Falls back to
    /// the raw update-date string only when a version comparison isn't possible on either side.
    /// Inequality, not ordering: version schemes across sites are irreconcilable, so "different" is
    /// the only safe comparison — see the design doc for why.
    /// </summary>
    private static SiteUpdateCheckResult Compare(InstallRecord record, SiteObservation observation, DateTime checkedUtc, SiteModKey? resolvedKey)
    {
        UpdateTracking tracking = record.Tracking!;

        string? normalizedBaselineVersion = NormalizeVersion(tracking.BaselineVersion);
        string? normalizedObservedVersion = NormalizeVersion(observation.Version);

        if (normalizedBaselineVersion is not null && normalizedObservedVersion is not null)
        {
            SiteUpdateStatus status = string.Equals(normalizedBaselineVersion, normalizedObservedVersion, StringComparison.OrdinalIgnoreCase)
                ? SiteUpdateStatus.UpToDate
                : SiteUpdateStatus.UpdateAvailable;
            return new SiteUpdateCheckResult(record.InstallId, status, observation.Version, observation.UpdatedOnRaw, null, checkedUtc, resolvedKey);
        }

        if (!string.IsNullOrWhiteSpace(tracking.BaselineUpdatedOnRaw) && !string.IsNullOrWhiteSpace(observation.UpdatedOnRaw))
        {
            SiteUpdateStatus status = string.Equals(tracking.BaselineUpdatedOnRaw, observation.UpdatedOnRaw, StringComparison.Ordinal)
                ? SiteUpdateStatus.UpToDate
                : SiteUpdateStatus.UpdateAvailable;
            return new SiteUpdateCheckResult(record.InstallId, status, observation.Version, observation.UpdatedOnRaw, null, checkedUtc, resolvedKey);
        }

        return new SiteUpdateCheckResult(
            record.InstallId,
            SiteUpdateStatus.Indeterminate,
            observation.Version,
            observation.UpdatedOnRaw,
            "The site gave no version or update date to compare against.",
            checkedUtc,
            resolvedKey);
    }

    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string trimmed = version.Trim();
        return trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V') ? trimmed[1..] : trimmed;
    }

    private static SiteUpdateCheckResult Indeterminate(InstallRecord record, string reason, DateTime checkedUtc, SiteModKey? resolvedKey = null) =>
        new(record.InstallId, SiteUpdateStatus.Indeterminate, null, null, reason, checkedUtc, resolvedKey);

    private async Task PersistCheckStateAsync(IReadOnlyList<SiteUpdateCheckResult> results, DateTime checkedUtc, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return;
        }

        Dictionary<string, UpdateCheckState> state = new(await stateStore.LoadAsync(cancellationToken), StringComparer.Ordinal);
        foreach (SiteUpdateCheckResult result in results)
        {
            state[result.InstallId] = new UpdateCheckState(
                result.InstallId,
                result.Status,
                result.ObservedVersion,
                result.ObservedUpdatedOnRaw,
                result.Status == SiteUpdateStatus.Indeterminate ? result.Reason : null,
                checkedUtc);
        }

        await stateStore.SaveAsync(state, cancellationToken);
    }
}
