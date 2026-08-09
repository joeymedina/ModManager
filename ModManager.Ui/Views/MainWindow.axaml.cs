using System.ComponentModel;
using Avalonia.Controls;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<ViewModelBase, Control> _pageViews = [];
    private readonly ViewLocator _viewLocator = new();
    private MainViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is MainViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ShowPage(viewModel.CurrentPage);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage) && _subscribedViewModel is not null)
        {
            ShowPage(_subscribedViewModel.CurrentPage);
        }
    }

    // Pages are built once and kept alive for the rest of the session instead
    // of being torn down and rebuilt by a plain ContentControl-bound Content
    // swap every time the user navigates away and back. That previously reset
    // page state on return — most visibly, the Browse page's WebViews were
    // destroyed and reloaded from scratch.
    private void ShowPage(ViewModelBase page)
    {
        if (!_pageViews.TryGetValue(page, out Control? view))
        {
            view = (Control)_viewLocator.Build(page)!;
            view.DataContext = page;
            _pageViews[page] = view;
            PageHost.Children.Add(view);
        }

        foreach (KeyValuePair<ViewModelBase, Control> pair in _pageViews)
        {
            pair.Value.IsVisible = ReferenceEquals(pair.Key, page);
        }
    }
}
