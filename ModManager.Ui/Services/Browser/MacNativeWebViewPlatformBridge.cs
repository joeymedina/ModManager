using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Services.Browser;

public sealed class MacNativeWebViewPlatformBridge : INativeWebViewPlatformBridge
{
    private readonly AdBlockService _adBlockService;
    private readonly Func<BrowsePageViewModel?> _getViewModel;
    private readonly Action<string> _onAdBlocked;
    private NativeWebView? _browser;

    public MacNativeWebViewPlatformBridge(
        AdBlockService adBlockService,
        Func<BrowsePageViewModel?> getViewModel,
        Action<string> onAdBlocked)
    {
        _adBlockService = adBlockService;
        _getViewModel = getViewModel;
        _onAdBlocked = onAdBlocked;
    }

    public void Attach(NativeWebView browser)
    {
        _browser = browser;
        browser.NavigationCompleted += OnNavigationCompleted;
        browser.WebMessageReceived += OnWebMessageReceived;
        browser.WebResourceRequested += OnWebResourceRequested;
    }

    public void CancelDownload()
    {
    }

    public void Dispose()
    {
        if (_browser is null)
        {
            return;
        }

        _browser.NavigationCompleted -= OnNavigationCompleted;
        _browser.WebMessageReceived -= OnWebMessageReceived;
        _browser.WebResourceRequested -= OnWebResourceRequested;
        _browser = null;
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _browser is null)
        {
            return;
        }

        await _browser.InvokeScript(BuildPageHookScript(_adBlockService.GetBlockedHosts()));
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (!TryGetMessage(e.Body, out string? kind, out Uri? uri) || uri is null)
        {
            return;
        }

        if (string.Equals(kind, "download", StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() => _getViewModel()?.TryBeginDownload(uri));
            return;
        }

        if (string.Equals(kind, "adBlocked", StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() => _onAdBlocked(uri.ToString()));
        }
    }

    private void OnWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
    {
        Uri uri = e.Request.Uri;
        if (!_adBlockService.IsBlocked(uri))
        {
            return;
        }

        _onAdBlocked(uri.ToString());
        _ = RemoveObservedAdAsync(uri);
    }

    private async Task RemoveObservedAdAsync(Uri uri)
    {
        if (_browser is null)
        {
            return;
        }

        string jsonUrl = JsonSerializer.Serialize(uri.ToString());
        string script = $$"""
            (() => {
                const url = {{jsonUrl}};
                document.querySelectorAll(`iframe[src="${url}"], img[src="${url}"], script[src="${url}"], link[href="${url}"]`)
                    .forEach(element => element.remove());
            })();
            """;
        await _browser.InvokeScript(script);
    }

    private static string BuildPageHookScript(IReadOnlyList<string> blockedHosts)
    {
        string blockedHostsJson = JsonSerializer.Serialize(blockedHosts);
        return $$"""
            (() => {
                if (window.__modManagerHooksInstalled) {
                    return;
                }

                window.__modManagerHooksInstalled = true;
                const blockedHosts = new Set({{blockedHostsJson}});
                const transparentPixel = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
                const downloadPattern = /\.(zip|rar|7z|tar|gz|tgz|bz2|xz|package|ts4script|sims3pack|trayitem|blueprint|bpi|exe|msi|dmg|pkg|deb|rpm|appimage|pdf|docx?|xlsx?|csv|txt|png|jpe?g|gif|webp|mp3|mp4|mov|avi|json|xml|ya?ml)(?:$|[?#])/i;

                const normalizeUrl = (value) => {
                    try {
                        return new URL(value, window.location.href);
                    } catch {
                        return null;
                    }
                };

                const report = (kind, value) => {
                    try {
                        invokeCSharpAction(JSON.stringify({ kind, url: value }));
                    } catch {
                    }
                };

                const isBlockedUrl = (value) => {
                    const url = normalizeUrl(value);
                    if (!url || (url.protocol !== 'http:' && url.protocol !== 'https:')) {
                        return false;
                    }

                    const host = url.hostname.toLowerCase();
                    for (const blockedHost of blockedHosts) {
                        if (host === blockedHost || host.endsWith('.' + blockedHost)) {
                            return true;
                        }
                    }

                    return false;
                };

                const neutralizeElement = (element, url) => {
                    if (!element || !url || !isBlockedUrl(url)) {
                        return false;
                    }

                    report('adBlocked', normalizeUrl(url).toString());

                    if (element instanceof HTMLImageElement) {
                        element.src = transparentPixel;
                    } else if (element instanceof HTMLIFrameElement) {
                        element.src = 'about:blank';
                    } else {
                        element.remove();
                    }

                    return true;
                };

                const inspectElement = (element) => {
                    if (!(element instanceof Element)) {
                        return;
                    }

                    if (element.matches('[src]')) {
                        neutralizeElement(element, element.getAttribute('src'));
                    }

                    if (element.matches('link[href]')) {
                        neutralizeElement(element, element.getAttribute('href'));
                    }

                    for (const child of element.querySelectorAll('[src],link[href]')) {
                        inspectElement(child);
                    }
                };

                const originalFetch = window.fetch;
                window.fetch = function(resource, options) {
                    const value = typeof resource === 'string' ? resource : resource?.url;
                    if (value && isBlockedUrl(value)) {
                        report('adBlocked', normalizeUrl(value).toString());
                        return Promise.resolve(new Response('', { status: 204, statusText: 'Blocked' }));
                    }

                    return originalFetch.call(this, resource, options);
                };

                const originalOpen = XMLHttpRequest.prototype.open;
                const originalSend = XMLHttpRequest.prototype.send;
                XMLHttpRequest.prototype.open = function(method, url, async, user, password) {
                    this.__modManagerBlockedUrl = url && isBlockedUrl(url) ? normalizeUrl(url).toString() : null;
                    return originalOpen.call(this, method, url, async, user, password);
                };
                XMLHttpRequest.prototype.send = function(body) {
                    if (this.__modManagerBlockedUrl) {
                        report('adBlocked', this.__modManagerBlockedUrl);
                        this.abort();
                        return;
                    }

                    return originalSend.call(this, body);
                };

                const originalSetAttribute = Element.prototype.setAttribute;
                Element.prototype.setAttribute = function(name, value) {
                    if ((name === 'src' || name === 'href') && neutralizeElement(this, value)) {
                        return;
                    }

                    return originalSetAttribute.call(this, name, value);
                };

                const wireProperty = (prototype, propertyName) => {
                    const descriptor = Object.getOwnPropertyDescriptor(prototype, propertyName);
                    if (!descriptor || typeof descriptor.set !== 'function') {
                        return;
                    }

                    Object.defineProperty(prototype, propertyName, {
                        configurable: true,
                        enumerable: descriptor.enumerable,
                        get: descriptor.get,
                        set(value) {
                            if (!neutralizeElement(this, value)) {
                                descriptor.set.call(this, value);
                            }
                        }
                    });
                };

                wireProperty(HTMLImageElement.prototype, 'src');
                wireProperty(HTMLScriptElement.prototype, 'src');
                wireProperty(HTMLIFrameElement.prototype, 'src');
                wireProperty(HTMLLinkElement.prototype, 'href');

                document.addEventListener('click', function(event) {
                    const anchor = event.target.closest('a[href]');
                    if (!anchor) {
                        return;
                    }

                    const href = anchor.href;
                    if (!href || !downloadPattern.test(href)) {
                        return;
                    }

                    event.preventDefault();
                    report('download', href);
                }, true);

                inspectElement(document.documentElement);

                const observer = new MutationObserver((mutations) => {
                    for (const mutation of mutations) {
                        if (mutation.type === 'attributes' && mutation.target instanceof Element) {
                            inspectElement(mutation.target);
                        }

                        for (const node of mutation.addedNodes) {
                            inspectElement(node);
                        }
                    }
                });

                observer.observe(document.documentElement, {
                    childList: true,
                    subtree: true,
                    attributes: true,
                    attributeFilter: ['src', 'href']
                });
            })();
            """;
    }

    private static bool TryGetMessage(string? message, out string? kind, out Uri? uri)
    {
        kind = null;
        uri = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        using JsonDocument json = JsonDocument.Parse(message);
        if (!json.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
            !json.RootElement.TryGetProperty("url", out JsonElement url))
        {
            return false;
        }

        kind = kindElement.GetString();
        if (!Uri.TryCreate(url.GetString(), UriKind.Absolute, out uri))
        {
            return false;
        }

        return string.Equals(kind, "adBlocked", StringComparison.Ordinal) ||
               (string.Equals(kind, "download", StringComparison.Ordinal) &&
                BrowserDownloadService.LooksLikeDownload(uri));
    }
}
