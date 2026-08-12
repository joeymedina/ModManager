using System.Net;
using Avalonia.Controls;

namespace ModManager.Ui.Services.Browser;

public interface IBrowsePageBrowser : IDisposable
{
    Control View { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    event Action? NavigationStarted;
    event Action<Uri?, bool>? NavigationCompleted;
    event Action<string>? AdBlocked;
    event Action<Uri>? NewTabRequested;
    void Navigate(Uri uri);
    void GoBack();
    void GoForward();
    void Refresh();
    Task<IReadOnlyList<Cookie>> GetCookiesAsync();
    void CancelDownload();
}
