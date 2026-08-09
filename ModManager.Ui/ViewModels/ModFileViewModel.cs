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

    public ModFileViewModel(ModFile file)
    {
        Apply(file);
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
        StatusText = file.IsConflicted
            ? "Conflicted"
            : file.State == ModFileState.Enabled
                ? "Enabled"
                : "Disabled";
        HasDepthWarning = string.Equals(Extension, ".ts4script", StringComparison.OrdinalIgnoreCase)
            && Folder.Contains('/');
    }
}
