using CommunityToolkit.Mvvm.ComponentModel;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ArchiveEntryPreviewViewModel : ViewModelBase
{
    public string EntryName { get; }

    public ArchiveEntryKind Kind { get; }

    public bool IsInstallable => Kind != ArchiveEntryKind.NotInstallable;

    public string KindLabel => Kind switch
    {
        ArchiveEntryKind.Variant => "Variant — review before installing",
        ArchiveEntryKind.NotInstallable => "Not a mod file",
        _ => "Installable",
    };

    [ObservableProperty]
    private bool _isSelected;

    public ArchiveEntryPreviewViewModel(ArchiveEntryPreview preview)
    {
        EntryName = preview.EntryName;
        Kind = preview.Kind;
        IsSelected = preview.SelectedByDefault;
    }
}
