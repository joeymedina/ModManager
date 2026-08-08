using Avalonia.Controls;

namespace ModManager.Ui.Services.Browser;

public sealed class NoopNativeWebViewPlatformBridge : INativeWebViewPlatformBridge
{
    public void Attach(NativeWebView browser)
    {
    }

    public void CancelDownload()
    {
    }

    public void Dispose()
    {
    }
}
