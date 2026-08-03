using CommunityToolkit.Mvvm.ComponentModel;

namespace ModManager.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
