using CommunityToolkit.Mvvm.ComponentModel;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ModFileViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private string _extension = string.Empty;

    [ObservableProperty]
    private long _sizeBytes;

    [ObservableProperty]
    private DateTime _modifiedUtc;

    [ObservableProperty]
    private ModFileState _state;

    [ObservableProperty]
    private bool _isConflicted;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasDepthWarning;

    [ObservableProperty]
    private string? _displayName;

    [ObservableProperty]
    private string? _groupId;

    [ObservableProperty]
    private string? _installId;

    [ObservableProperty]
    private string? _version;

    [ObservableProperty]
    private DateTime? _installedUtc;

    [ObservableProperty]
    private string? _provider;

    [ObservableProperty]
    private string? _modPageUrl;

    /// <summary>
    /// Two-way target for the per-row on/off switch. The setter only records what the row should
    /// look like — <see cref="ModsPageViewModel"/> owns the actual move and re-reads state on refresh.
    /// </summary>
    public bool IsEnabled => State == ModFileState.Enabled;

    public string SizeText => FormatSize(SizeBytes);

    public string ModifiedText => ModifiedUtc.ToLocalTime().ToString("g");

    public string InstalledText => InstalledUtc is { } installed ? installed.ToLocalTime().ToString("g") : string.Empty;

    public string FolderText => string.IsNullOrEmpty(Folder) ? "(root)" : Folder;

    public bool HasModPage => !string.IsNullOrWhiteSpace(ModPageUrl);

    public ModFileViewModel(ModFile file)
    {
        Apply(file);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    public void Apply(ModFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        RelativePath = file.RelativePath;
        Name = Path.GetFileName(file.RelativePath);
        Folder = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        Extension = Path.GetExtension(file.RelativePath);
        SizeBytes = file.SizeBytes;
        ModifiedUtc = file.ModifiedUtc;
        State = file.State;
        IsConflicted = file.IsConflicted;
        Refresh();
        HasDepthWarning = string.Equals(Extension, ".ts4script", StringComparison.OrdinalIgnoreCase)
            && Folder.Contains('/');
        DisplayName = file.DisplayName;
        GroupId = file.GroupId;
        InstallId = file.InstallId;
        Version = file.Version;
        InstalledUtc = file.InstalledUtc;
        Provider = file.Provider;
        ModPageUrl = file.ModPageUrl;
    }

    /// <summary>Recomputes the derived status text after <see cref="State"/> is changed in place.</summary>
    public void Refresh() => StatusText = IsConflicted
        ? "Conflicted"
        : State == ModFileState.Enabled
            ? "Enabled"
            : "Disabled";

    partial void OnStateChanged(ModFileState value) => OnPropertyChanged(nameof(IsEnabled));

    partial void OnSizeBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));

    partial void OnModifiedUtcChanged(DateTime value) => OnPropertyChanged(nameof(ModifiedText));

    partial void OnInstalledUtcChanged(DateTime? value) => OnPropertyChanged(nameof(InstalledText));

    partial void OnFolderChanged(string value) => OnPropertyChanged(nameof(FolderText));

    partial void OnModPageUrlChanged(string? value) => OnPropertyChanged(nameof(HasModPage));
}
