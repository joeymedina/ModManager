using Avalonia.Controls;

namespace ModManager.Ui.Services.Browser;

public interface INativeWebViewPlatformBridge : IDisposable
{
    void Attach(NativeWebView browser);
    void CancelDownload();
}
