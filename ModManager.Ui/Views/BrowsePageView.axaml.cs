using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Views;

public partial class BrowsePageView : UserControl
{
    private readonly Dictionary<BrowserTabViewModel, BrowserTabView> _tabViews = new();
    private BrowsePageViewModel? _subscribedViewModel;

    public BrowsePageView()
    {
        InitializeComponent();
    }

    private BrowsePageViewModel? ViewModel => DataContext as BrowsePageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Tabs.CollectionChanged -= OnTabsCollectionChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        ClearTabViews();

        if (DataContext is BrowsePageViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.Tabs.CollectionChanged += OnTabsCollectionChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            foreach (BrowserTabViewModel tab in viewModel.Tabs)
            {
                AddTabView(tab);
            }

            UpdateSelectedTabVisibility();
        }
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BrowserTabViewModel tab in e.OldItems)
            {
                RemoveTabView(tab);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BrowserTabViewModel tab in e.NewItems)
            {
                AddTabView(tab);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowsePageViewModel.SelectedTab))
        {
            UpdateSelectedTabVisibility();
        }
    }

    private void AddTabView(BrowserTabViewModel tab)
    {
        BrowserTabView view = new() { DataContext = tab, IsVisible = false };
        _tabViews[tab] = view;
        TabHost.Children.Add(view);
        UpdateSelectedTabVisibility();
    }

    private void RemoveTabView(BrowserTabViewModel tab)
    {
        if (_tabViews.Remove(tab, out BrowserTabView? view))
        {
            TabHost.Children.Remove(view);
        }
    }

    private void ClearTabViews()
    {
        TabHost.Children.Clear();
        _tabViews.Clear();
    }

    private void UpdateSelectedTabVisibility()
    {
        BrowserTabViewModel? selected = ViewModel?.SelectedTab;
        foreach (KeyValuePair<BrowserTabViewModel, BrowserTabView> pair in _tabViews)
        {
            pair.Value.IsVisible = ReferenceEquals(pair.Key, selected);
        }
    }

    private BrowserTabView? SelectedTabView =>
        ViewModel?.SelectedTab is { } tab && _tabViews.TryGetValue(tab, out BrowserTabView? view)
            ? view
            : null;

    private void OnBackClick(object? sender, RoutedEventArgs e) => SelectedTabView?.GoBack();

    private void OnForwardClick(object? sender, RoutedEventArgs e) => SelectedTabView?.GoForward();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => SelectedTabView?.Refresh();

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ViewModel?.SelectedTab?.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTabHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BrowserTabViewModel tab })
        {
            ViewModel?.SelectTabCommand.Execute(tab);
        }
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BrowserTabViewModel tab })
        {
            tab.CloseCommand.Execute(null);
        }
    }
}
