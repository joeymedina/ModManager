using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

/// <summary>
/// Lists installs tracked against a site (see <see cref="InstallRecord.Tracking"/>) and checks them
/// against <see cref="IModSiteUpdateService"/> on demand — v1 has no launch check or background sweep,
/// only this page's manual button. Detect-only: there is no "install the update" action here, only
/// "open the mod page" so the user downloads it themselves through the app's own Browse tab (via
/// <see cref="OpenModPageRequested"/>, handled by <see cref="MainViewModel"/>), same as
/// <see cref="ModsPageViewModel.OpenModPageCommand"/> already does for a single mod's page.
/// </summary>
public partial class UpdatesPageViewModel : ViewModelBase
{
    private readonly ModsPageViewModel _mods;
    private readonly IModSiteUpdateService _updateService;
    private readonly ILogger<UpdatesPageViewModel> _logger;

    [ObservableProperty]
    private string _statusMessage = "Check for mod updates here.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasRows;

    public ObservableCollection<UpdateRowViewModel> Rows { get; } = [];

    public UpdatesPageViewModel()
        : this(new ModsPageViewModel(), new NoopModSiteUpdateService())
    {
    }

    public UpdatesPageViewModel(ModsPageViewModel mods, IModSiteUpdateService updateService, ILogger<UpdatesPageViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(updateService);

        _mods = mods;
        _updateService = updateService;
        _logger = logger ?? NullLogger<UpdatesPageViewModel>.Instance;

        // XAML binds on this bool rather than Rows.Count directly, to avoid relying on a
        // negated-non-bool binding expression that isn't a pattern proven elsewhere in this app.
        Rows.CollectionChanged += (_, _) => HasRows = Rows.Count > 0;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Checking for updates…";

        try
        {
            ModsManifest manifest = await _mods.LoadManifestAsync();
            List<TrackedMod> trackedMods = [.. manifest.Installs
                .Where(record => record.Tracking is not null)
                .Select(record => new TrackedMod(record, ResolveDisplayName(record, manifest)))];

            if (trackedMods.Count == 0)
            {
                Rows.Clear();
                StatusMessage = "No mods are linked to a site yet. Link one from the Mods page to start checking for updates.";
                return;
            }

            IReadOnlyList<SiteUpdateCheckResult> results = await _updateService.CheckAsync(trackedMods, CancellationToken.None);
            await PersistNewlyResolvedKeysAsync(trackedMods, results);

            Dictionary<string, SiteUpdateCheckResult> resultByInstallId = results.ToDictionary(result => result.InstallId, StringComparer.Ordinal);
            RebuildRows(trackedMods, resultByInstallId);

            int updateCount = results.Count(result => result.IsUpdateAvailable);
            StatusMessage = updateCount switch
            {
                0 => $"Checked {trackedMods.Count} mod(s). Everything is up to date.",
                1 => "Checked mods. 1 update available.",
                _ => $"Checked mods. {updateCount} updates available."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            StatusMessage = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Raised so <see cref="MainViewModel"/> can switch to the Browse tab and open the URL there.</summary>
    public event Action<Uri>? OpenModPageRequested;

    [RelayCommand]
    private void OpenModPage(UpdateRowViewModel? row)
    {
        if (row is null || !Uri.TryCreate(row.TrackingUrl, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        OpenModPageRequested?.Invoke(uri);
    }

    [RelayCommand(CanExecute = nameof(CanMarkRowAsCurrent))]
    private async Task MarkAsCurrentAsync(UpdateRowViewModel? row)
    {
        if (row is null || !row.CanMarkAsCurrent)
        {
            return;
        }

        UpdateTracking updated = row.Tracking with
        {
            BaselineVersion = row.ObservedVersion ?? row.Tracking.BaselineVersion,
            BaselineUpdatedOnRaw = row.ObservedUpdatedOnRaw ?? row.Tracking.BaselineUpdatedOnRaw,
            BaselineCapturedUtc = DateTime.UtcNow
        };

        ArchiveInstallResult<InstallRecord> result = await _mods.UpdateInstallTrackingAsync(row.InstallId, updated);
        if (result.Success)
        {
            row.ApplyMarkedAsCurrent(updated);
            StatusMessage = $"Marked \"{row.DisplayName}\" as up to date.";
        }
        else
        {
            StatusMessage = result.Error ?? "Could not update.";
        }
    }

    private static bool CanMarkRowAsCurrent(UpdateRowViewModel? row) => row?.CanMarkAsCurrent == true;

    /// <summary>
    /// A key <see cref="IModSiteUpdateService"/> resolves this check (see
    /// <see cref="SiteUpdateCheckResult.ResolvedModKey"/>) is reported back, not written back — the
    /// service stays manifest-write-free like the strategies it calls, so the caller persists it.
    /// </summary>
    private async Task PersistNewlyResolvedKeysAsync(IReadOnlyList<TrackedMod> trackedMods, IReadOnlyList<SiteUpdateCheckResult> results)
    {
        foreach (SiteUpdateCheckResult result in results)
        {
            if (result.ResolvedModKey is not { } resolvedKey)
            {
                continue;
            }

            TrackedMod? mod = trackedMods.FirstOrDefault(candidate => candidate.Record.InstallId == result.InstallId);
            if (mod is null)
            {
                continue;
            }

            UpdateTracking updated = mod.Record.Tracking! with { SiteModKey = resolvedKey.Value };
            ArchiveInstallResult<InstallRecord> saveResult = await _mods.UpdateInstallTrackingAsync(mod.Record.InstallId, updated);
            if (!saveResult.Success)
            {
                _logger.LogWarning("Could not persist resolved mod key for install {InstallId}: {Error}", mod.Record.InstallId, saveResult.Error);
            }
        }
    }

    private void RebuildRows(IReadOnlyList<TrackedMod> trackedMods, IReadOnlyDictionary<string, SiteUpdateCheckResult> resultByInstallId)
    {
        Dictionary<string, UpdateRowViewModel> existingByInstallId = Rows.ToDictionary(row => row.InstallId, StringComparer.Ordinal);
        Rows.Clear();

        foreach (TrackedMod mod in trackedMods)
        {
            if (!existingByInstallId.TryGetValue(mod.Record.InstallId, out UpdateRowViewModel? row))
            {
                row = new UpdateRowViewModel(mod.Record, mod.DisplayName);
            }
            else
            {
                row.Apply(mod.Record, mod.DisplayName);
            }

            if (resultByInstallId.TryGetValue(mod.Record.InstallId, out SiteUpdateCheckResult? result))
            {
                row.ApplyResult(result);
            }

            Rows.Add(row);
        }
    }

    /// <summary>
    /// Every file an install wrote shares the same manifest-recorded display name (see
    /// `ArchiveInstallService.PersistRecordAsync`), so the first file's entry is enough. Falls back to
    /// the install's folder name for a record with no manifest entry at all (hand-edited manifest, or
    /// a path that no longer resolves).
    /// </summary>
    private static string ResolveDisplayName(InstallRecord record, ModsManifest manifest)
    {
        if (record.Files.Count == 0)
        {
            return "(unknown mod)";
        }

        string firstPath = record.Files[0].RelativePath;
        ManifestFileEntry? entry = manifest.Files.FirstOrDefault(file => string.Equals(file.RelativePath, firstPath, StringComparison.OrdinalIgnoreCase));
        if (entry?.DisplayName is { Length: > 0 } displayName)
        {
            return displayName;
        }

        return firstPath.Split('/', 2)[0];
    }

    private sealed class NoopModSiteUpdateService : IModSiteUpdateService
    {
        public Task<IReadOnlyList<SiteUpdateCheckResult>> CheckAsync(IReadOnlyList<TrackedMod> trackedMods, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SiteUpdateCheckResult>>([]);
    }
}
