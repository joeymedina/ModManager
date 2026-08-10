using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

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
        : this(new ModsPageViewModel(), new UpdatesPageViewModel(), new BrowsePageViewModel(), new SettingsPageViewModel())
    {
    }

    public MainViewModel(
        ModsPageViewModel modsPage,
        UpdatesPageViewModel updatesPage,
        BrowsePageViewModel browsePage,
        SettingsPageViewModel settingsPage)
    {
        ArgumentNullException.ThrowIfNull(modsPage);
        ArgumentNullException.ThrowIfNull(updatesPage);
        ArgumentNullException.ThrowIfNull(browsePage);
        ArgumentNullException.ThrowIfNull(settingsPage);

        _modsPage = modsPage;

        NavigationItems =
        [
            new NavigationItemViewModel("Mods", modsPage, FASymbol.Folder),
            new NavigationItemViewModel("Updates", updatesPage, FASymbol.Sync),
            new NavigationItemViewModel("Browse", browsePage, FASymbol.Globe),
            new NavigationItemViewModel("Settings", settingsPage, FASymbol.Settings)
        ];

        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = SelectedNavigationItem.Page;

        browsePage.InstallRequested += OnInstallRequested;

        // The mods folder is remembered between launches, so show its contents without making the
        // user hunt for a Refresh button on every start.
        _ = modsPage.RefreshCommand.ExecuteAsync(null);
    }

    private void OnInstallRequested(string filePath, Uri? sourceUri, Uri? modPageUri)
    {
        SelectedNavigationItem = NavigationItems[0];
        _modsPage.BeginInstallFromFile(filePath, sourceUri, modPageUri);
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }
}
