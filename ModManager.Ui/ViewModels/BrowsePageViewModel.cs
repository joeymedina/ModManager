using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public partial class BrowsePageViewModel : ViewModelBase
{
    private readonly BrowserDownloadService _downloadService;
    private readonly ILogger<BrowserTabViewModel>? _tabLogger;

    [ObservableProperty]
    private BrowserTabViewModel? _selectedTab;

    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public event Action<string, Uri?, Uri?>? InstallRequested;

    public BrowsePageViewModel()
        : this(new BrowserDownloadService())
    {
    }

    public BrowsePageViewModel(BrowserDownloadService downloadService, ILogger<BrowserTabViewModel>? tabLogger = null)
    {
        _downloadService = downloadService;
        _tabLogger = tabLogger;
        AddTab();
    }

    partial void OnSelectedTabChanged(BrowserTabViewModel? oldValue, BrowserTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    [RelayCommand]
    private void NewTab()
    {
        AddTab();
    }

    [RelayCommand]
    private void SelectTab(BrowserTabViewModel? tab)
    {
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    private BrowserTabViewModel AddTab()
    {
        BrowserTabViewModel tab = new(_downloadService, BeginDownload, _tabLogger);
        tab.CloseRequested += OnTabCloseRequested;
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    private DownloadItemViewModel BeginDownload(string fileName, Uri? sourceUri, Action cancelRequested)
    {
        DownloadItemViewModel item = new(fileName, sourceUri, cancelRequested);
        item.InstallRequested += (filePath, sourceUri, modPageUri) => InstallRequested?.Invoke(filePath, sourceUri, modPageUri);
        Downloads.Insert(0, item);
        return item;
    }

    private void OnTabCloseRequested(BrowserTabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        int index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        tab.CloseRequested -= OnTabCloseRequested;
        Tabs.RemoveAt(index);

        if (SelectedTab == tab)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }
}
