using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ModManager.Ui.Services.Browser;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Views;

public partial class BrowserTabView : UserControl
{
    private BrowserTabViewModel? _subscribedViewModel;
    private bool _hasLoadedInitialPage;
    private readonly IBrowsePageBrowser _browser;

    public BrowserTabView()
    {
        InitializeComponent();
        _browser = new AvaloniaBrowsePageBrowser(() => ViewModel);
        _browser.NavigationStarted += OnBrowserNavigationStarted;
        _browser.NavigationCompleted += OnBrowserNavigationCompleted;
        _browser.AdBlocked += OnBrowserAdBlocked;
        _browser.NewTabRequested += OnBrowserNewTabRequested;
        BrowserHost.Child = _browser.View;
    }

    private BrowserTabViewModel? ViewModel => DataContext as BrowserTabViewModel;

    public bool CanGoBack => _browser.CanGoBack;

    public bool CanGoForward => _browser.CanGoForward;

    public void GoBack()
    {
        _browser.GoBack();
        SyncNavigationButtons();
    }

    public void GoForward()
    {
        _browser.GoForward();
        SyncNavigationButtons();
    }

    public void Refresh() => _browser.Refresh();

    public void Navigate(Uri uri) => _browser.Navigate(uri);

    public void CancelDownload() => _browser.CancelDownload();

    public Task<IReadOnlyList<Cookie>> GetCookiesAsync() => _browser.GetCookiesAsync();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.NavigationRequested -= OnNavigationRequested;
            _subscribedViewModel.BrowserDownloadCancellationRequested -= OnBrowserDownloadCancellationRequested;
            _subscribedViewModel.CookiesRequested -= OnCookiesRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is BrowserTabViewModel viewModel)
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
        Uri uri = Uri.TryCreate(ViewModel.AddressText, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : BrowserTabViewModel.DefaultHome;
        _browser.Navigate(uri);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _browser.Dispose();
    }

    private void OnNavigationRequested(object? sender, Uri uri)
    {
        _browser.Navigate(uri);
    }

    private void OnBrowserDownloadCancellationRequested()
    {
        _browser.CancelDownload();
    }

    private Task<IReadOnlyList<Cookie>> OnCookiesRequested() => _browser.GetCookiesAsync();

    private void OnBrowserNavigationStarted()
    {
        ViewModel?.OnNavigationStarted();
    }

    private void OnBrowserNavigationCompleted(Uri? uri, bool isSuccess)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.OnNavigationCompleted(uri, isSuccess);
        SyncNavigationButtons();
    }

    private void OnBrowserAdBlocked(string url)
    {
        ViewModel?.OnAdBlocked(url);
    }

    private void OnBrowserNewTabRequested(Uri uri)
    {
        ViewModel?.OnNewTabRequested(uri);
    }

    private void SyncNavigationButtons()
    {
        ViewModel?.UpdateNavigationState(_browser.CanGoBack, _browser.CanGoForward);
    }
}
