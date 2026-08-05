using CommunityToolkit.Mvvm.ComponentModel;

namespace ModManager.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    public MainViewModel()
        : this(new ModPageViewModel())
    {
    }

    public MainViewModel(ModPageViewModel modPage)
    {
        ArgumentNullException.ThrowIfNull(modPage);
        CurrentPage = modPage;
    }
}
