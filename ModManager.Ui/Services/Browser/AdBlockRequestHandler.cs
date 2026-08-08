using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;
using ModManager.Ui.Services;

namespace ModManager.Ui.Services.Browser;

public sealed class AdBlockRequestHandler : RequestHandler
{
    private readonly AdBlockService _service;
    private readonly Action<string> _onBlocked;

    public AdBlockRequestHandler(AdBlockService service, Action<string> onBlocked)
    {
        _service = service;
        _onBlocked = onBlocked;
    }

    public Task RefreshAsync() => _service.RefreshAsync();

    protected override CefResourceRequestHandler GetResourceRequestHandler(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling)
    {
        return new AdBlockResourceRequestHandler(_service, _onBlocked);
    }
}
