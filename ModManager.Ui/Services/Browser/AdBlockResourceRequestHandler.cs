using Xilium.CefGlue;
using ModManager.Ui.Services;

namespace ModManager.Ui.Services.Browser;

public sealed class AdBlockResourceRequestHandler : CefResourceRequestHandler
{
    private readonly AdBlockService _service;
    private readonly Action<string> _onBlocked;

    public AdBlockResourceRequestHandler(AdBlockService service, Action<string> onBlocked)
    {
        _service = service;
        _onBlocked = onBlocked;
    }

    protected override CefCookieAccessFilter? GetCookieAccessFilter(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request) => null;

    protected override CefReturnValue OnBeforeResourceLoad(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request,
        CefCallback callback)
    {
        if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri) &&
            _service.IsBlocked(uri))
        {
            _onBlocked(request.Url);
            return CefReturnValue.Cancel;
        }

        return CefReturnValue.Continue;
    }
}
