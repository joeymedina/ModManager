using CommunityToolkit.Mvvm.ComponentModel;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

/// <summary>
/// One row on the Updates page — a tracked install plus whatever its last check found. Mutated in
/// place (mirrors <see cref="ModFileViewModel.Apply"/>) rather than rebuilt, so "mark as current" can
/// update a row instantly without a second network round-trip through the strategy it just resolved.
/// </summary>
public partial class UpdateRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _installId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _trackingUrl = string.Empty;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _observedVersion;

    [ObservableProperty]
    private string? _observedUpdatedOnRaw;

    [ObservableProperty]
    private SiteUpdateStatus _status = SiteUpdateStatus.Indeterminate;

    [ObservableProperty]
    private string _statusText = "Not checked yet";

    [ObservableProperty]
    private DateTime? _lastCheckedUtc;

    public UpdateTracking Tracking { get; private set; } = null!;

    public bool IsUpdateAvailable => Status == SiteUpdateStatus.UpdateAvailable;

    /// <summary>
    /// "Mark as current" only makes sense once a check has actually observed something to mark —
    /// before that there's nothing to correct.
    /// </summary>
    public bool CanMarkAsCurrent => ObservedVersion is not null || ObservedUpdatedOnRaw is not null;

    public string LastCheckedText => LastCheckedUtc is { } lastChecked ? lastChecked.ToLocalTime().ToString("g") : "Never";

    public string InstalledVersionText => InstalledVersion is { Length: > 0 } version ? version : "(unknown)";

    public UpdateRowViewModel(InstallRecord record, string displayName)
    {
        Apply(record, displayName);
    }

    public void Apply(InstallRecord record, string displayName)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Tracking);

        InstallId = record.InstallId;
        DisplayName = displayName;
        Tracking = record.Tracking;
        TrackingUrl = record.Tracking.TrackingUrl;
        InstalledVersion = record.Tracking.BaselineVersion;
    }

    public void ApplyResult(SiteUpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Status = result.Status;
        ObservedVersion = result.ObservedVersion;
        ObservedUpdatedOnRaw = result.ObservedUpdatedOnRaw;
        LastCheckedUtc = result.CheckedUtc;
        StatusText = result.Status switch
        {
            SiteUpdateStatus.UpToDate => "Up to date",
            SiteUpdateStatus.UpdateAvailable => ObservedVersion is { Length: > 0 } version ? $"Update available ({version})" : "Update available",
            _ => result.Reason is { Length: > 0 } reason ? reason : "Could not check"
        };
    }

    /// <summary>Applied after a successful "mark as current" write — no re-check needed.</summary>
    public void ApplyMarkedAsCurrent(UpdateTracking updatedTracking)
    {
        ArgumentNullException.ThrowIfNull(updatedTracking);

        Tracking = updatedTracking;
        InstalledVersion = updatedTracking.BaselineVersion;
        Status = SiteUpdateStatus.UpToDate;
        StatusText = "Up to date";
    }

    partial void OnStatusChanged(SiteUpdateStatus value) => OnPropertyChanged(nameof(IsUpdateAvailable));

    partial void OnObservedVersionChanged(string? value) => OnPropertyChanged(nameof(CanMarkAsCurrent));

    partial void OnObservedUpdatedOnRawChanged(string? value) => OnPropertyChanged(nameof(CanMarkAsCurrent));

    partial void OnLastCheckedUtcChanged(DateTime? value) => OnPropertyChanged(nameof(LastCheckedText));

    partial void OnInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(InstalledVersionText));
}
