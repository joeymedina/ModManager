using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Net;
using ModManager.Ui.Services.Browser;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Views;

public partial class BrowsePageView : UserControl
{
    private BrowsePageViewModel? _subscribedViewModel;
    private bool _hasLoadedInitialPage;
    private readonly IBrowsePageBrowser _browser;

    public BrowsePageView()
    {
        InitializeComponent();
        _browser = new AvaloniaBrowsePageBrowser(() => ViewModel);
        _browser.NavigationStarted += OnBrowserNavigationStarted;
        _browser.NavigationCompleted += OnBrowserNavigationCompleted;
        _browser.AdBlocked += OnBrowserAdBlocked;
        BrowserHost.Child = _browser.View;
    }

    private BrowsePageViewModel? ViewModel => DataContext as BrowsePageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.NavigationRequested -= OnNavigationRequested;
            _subscribedViewModel.BrowserDownloadCancellationRequested -= OnBrowserDownloadCancellationRequested;
            _subscribedViewModel.CookiesRequested -= OnCookiesRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is BrowsePageViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            _subscribedViewModel.NavigationRequested += OnNavigationRequested;
            _subscribedViewModel.BrowserDownloadCancellationRequested += OnBrowserDownloadCancellationRequested;
            _subscribedViewModel.CookiesRequested += OnCookiesRequested;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hasLoadedInitialPage || ViewModel is null)
        {
            return;
        }

        _hasLoadedInitialPage = true;
        _browser.Navigate(BrowsePageViewModel.DefaultHome);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {

        _browser.Dispose();
    }

    private void OnNavigationRequested(object? sender, Uri uri)
    {
        _browser.Navigate(uri);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _browser.GoBack();
        SyncNavigationButtons();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        _browser.GoForward();
        SyncNavigationButtons();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        _browser.Refresh();
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ViewModel?.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    private void OnBrowserNavigationStarted()
    {
        ViewModel?.OnNavigationStarted();
    }

    private void OnBrowserNavigationCompleted(Uri? uri, bool isSuccess)
    {
        if (ViewModel is null || ViewModel.IsDownloading)
        {
            return;
        }

        ViewModel.OnNavigationCompleted(uri, isSuccess);
        SyncNavigationButtons();
    }

    private void OnBrowserDownloadCancellationRequested()
    {
        _browser.CancelDownload();
    }

    private Task<IReadOnlyList<Cookie>> OnCookiesRequested() => _browser.GetCookiesAsync();

    private void OnBrowserAdBlocked(string url)
    {
        ViewModel?.OnAdBlocked(url);
    }

    private void SyncNavigationButtons()
    {
        ViewModel?.UpdateNavigationState(
            _browser.CanGoBack,
            _browser.CanGoForward);
    }
}
