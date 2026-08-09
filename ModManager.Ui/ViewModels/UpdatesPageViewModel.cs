using CommunityToolkit.Mvvm.ComponentModel;

namespace ModManager.Ui.ViewModels;

public partial class UpdatesPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = "Check for mod updates here.";
}
