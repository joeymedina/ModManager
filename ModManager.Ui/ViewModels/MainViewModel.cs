using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModManager.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ModsPageViewModel _modsPage;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public MainViewModel()
        : this(new ModsPageViewModel(), new UpdatesPageViewModel(), new BrowsePageViewModel())
    {
    }

    public MainViewModel(
        ModsPageViewModel modsPage,
        UpdatesPageViewModel updatesPage,
        BrowsePageViewModel browsePage)
    {
        ArgumentNullException.ThrowIfNull(modsPage);
        ArgumentNullException.ThrowIfNull(updatesPage);
        ArgumentNullException.ThrowIfNull(browsePage);

        _modsPage = modsPage;

        NavigationItems =
        [
            new NavigationItemViewModel("Mods", modsPage),
            new NavigationItemViewModel("Updates", updatesPage),
            new NavigationItemViewModel("Browse", browsePage)
        ];

        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = SelectedNavigationItem.Page;

        browsePage.InstallRequested += OnInstallRequested;
    }

    private void OnInstallRequested(string filePath)
    {
        SelectedNavigationItem = NavigationItems[0];
        _modsPage.BeginInstallFromFile(filePath);
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }
}
