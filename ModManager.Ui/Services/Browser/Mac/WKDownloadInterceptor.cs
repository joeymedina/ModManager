using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using ModManager.Ui.Services.Browser.Mac.Interop;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Services.Browser.Mac;

// Real macOS download interception. Avalonia's WKWebView wrapper implements no
// download handling at all (confirmed by reading their MaciosWebViewAdapter
// source), so this becomes the WKWebView's navigationDelegate itself to
// implement `decidePolicyForNavigationResponse` (a selector Avalonia's own
// delegate does not implement) and the WKDownloadDelegate methods for
// progress/completion/cancel.
//
// Because WKWebView.navigationDelegate is a single slot, replacing it would
// silently break Avalonia's own NavigationCompleted/WebResourceRequested
// events. This class instead captures Avalonia's original delegate and
// forwards every selector it doesn't implement itself back to it, via the
// standard Objective-C message-forwarding pair (forwardingTargetForSelector:
// + respondsToSelector:, the latter is required too — WKNavigationDelegate
// methods are @optional, and the default respondsToSelector: does not consult
// forwardingTargetForSelector: on its own).
//
// ponytail: no WKDownload progress KVO is wired up (macOS downloads show as
// "in progress" with no live percentage, then flip to complete/failed) —
// Windows still reports live percentage via CoreWebView2. Add KVO on
// WKDownload.progress if per-file percentage on macOS is needed later.
[SupportedOSPlatform("macos")]
internal sealed unsafe class WKDownloadInterceptor : IDisposable
{
    private const long PolicyCancel = 0;
    private const long PolicyAllow = 1;
    private const long PolicyDownload = 2;

    private static readonly IntPtr NsUrlClass = Libobjc.objc_getClass("NSURL");
    private static readonly IntPtr FileUrlWithPathSel = Libobjc.sel_getUid("fileURLWithPath:");
    private static readonly IntPtr ResponseSel = Libobjc.sel_getUid("response");
    private static readonly IntPtr UrlSel = Libobjc.sel_getUid("URL");
    private static readonly IntPtr AbsoluteStringSel = Libobjc.sel_getUid("absoluteString");
    private static readonly IntPtr CanShowMimeTypeSel = Libobjc.sel_getUid("canShowMIMEType");
    private static readonly IntPtr SetDelegateSel = Libobjc.sel_getUid("setDelegate:");
    private static readonly IntPtr CancelSel = Libobjc.sel_getUid("cancel:");
    private static readonly IntPtr NavigationDelegateSel = Libobjc.sel_getUid("navigationDelegate");
    private static readonly IntPtr SetNavigationDelegateSel = Libobjc.sel_getUid("setNavigationDelegate:");
    private static readonly IntPtr AllocSel = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr InitSel = Libobjc.sel_getUid("init");
    private static readonly IntPtr RetainSel = Libobjc.sel_getUid("retain");
    private static readonly IntPtr ReleaseSel = Libobjc.sel_getUid("release");
    private static readonly IntPtr RespondsToSelectorSel = Libobjc.sel_getUid("respondsToSelector:");

    private static readonly IntPtr s_class;

    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>
        s_decidePolicyForNavigationResponse = &OnDecidePolicyForNavigationResponse;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>
        s_didBecomeDownload = &OnDidBecomeDownload;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>
        s_decideDestination = &OnDecideDestination;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>
        s_downloadDidFinish = &OnDownloadDidFinish;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>
        s_downloadDidFail = &OnDownloadDidFail;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>
        s_forwardingTargetForSelector = &OnForwardingTargetForSelector;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>
        s_respondsToSelector = &OnRespondsToSelector;
    private static readonly unsafe IntPtr s_cancelCompletionCallback =
        new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnCancelCompleted);

    static WKDownloadInterceptor()
    {
        IntPtr cls = Libobjc.objc_allocateClassPair(
            Libobjc.objc_getClass("NSObject"),
            "ModManagerWKDownloadInterceptor",
            0);

        AddProtocol(cls, "WKNavigationDelegate");
        AddProtocol(cls, "WKDownloadDelegate");

        AddMethod(cls, "webView:decidePolicyForNavigationResponse:decisionHandler:", s_decidePolicyForNavigationResponse, "v@:@@@");
        AddMethod(cls, "webView:navigationResponse:didBecomeDownload:", s_didBecomeDownload, "v@:@@@");
        AddMethod(cls, "download:decideDestinationUsingResponse:suggestedFilename:completionHandler:", s_decideDestination, "v@:@@@@");
        AddMethod(cls, "downloadDidFinish:", s_downloadDidFinish, "v@:@");
        AddMethod(cls, "download:didFailWithError:resumeData:", s_downloadDidFail, "v@:@@@");
        AddMethod(cls, "forwardingTargetForSelector:", s_forwardingTargetForSelector, "@@::");
        AddMethod(cls, "respondsToSelector:", s_respondsToSelector, "B@::");

        bool ivarAdded = ManagedObjcClass.AddManagedSelfIvar(cls);
        Debug.Assert(ivarAdded);

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    private readonly Func<BrowserTabViewModel?> _getViewModel;
    private readonly IntPtr _webViewHandle;
    private readonly IntPtr _originalNavigationDelegate;
    private IntPtr _activeDownloadHandle;
    private Uri? _pendingUri;
    private string? _pendingFilePath;
    private bool _cancelRequested;

    public WKDownloadInterceptor(IntPtr webViewHandle, Func<BrowserTabViewModel?> getViewModel)
    {
        _getViewModel = getViewModel;
        _webViewHandle = webViewHandle;
        _originalNavigationDelegate = Libobjc.IntPtr_msgSend(webViewHandle, NavigationDelegateSel);

        IntPtr allocated = Libobjc.IntPtr_msgSend(s_class, AllocSel);
        Handle = Libobjc.IntPtr_msgSend(allocated, InitSel);
        Libobjc.IntPtr_msgSend(Handle, RetainSel);
        ManagedObjcClass.WriteManagedSelf(Handle, this);

        Libobjc.Void_msgSend(webViewHandle, SetNavigationDelegateSel, Handle);
    }

    public IntPtr Handle { get; }

    public void CancelActiveDownload()
    {
        if (_activeDownloadHandle == IntPtr.Zero)
        {
            return;
        }

        _cancelRequested = true;
        IntPtr completion = BlockLiteral.GetBlockForFunctionPointer(s_cancelCompletionCallback, IntPtr.Zero);
        Libobjc.Void_msgSend(_activeDownloadHandle, CancelSel, completion);
    }

    public void Dispose()
    {
        Libobjc.Void_msgSend(_webViewHandle, SetNavigationDelegateSel, IntPtr.Zero);
        ManagedObjcClass.FreeManagedSelf(Handle);
        Libobjc.Void_msgSend(Handle, ReleaseSel);
    }

    private static void AddProtocol(IntPtr cls, string protocolName)
    {
        IntPtr protocol = Libobjc.objc_getProtocol(protocolName);
        int result = Libobjc.class_addProtocol(cls, protocol);
        Debug.Assert(result == 1);
    }

    private static void AddMethod(IntPtr cls, string selectorName, void* impl, string types)
    {
        IntPtr selector = Libobjc.sel_getUid(selectorName);
        int result = Libobjc.class_addMethod(cls, selector, impl, types);
        Debug.Assert(result == 1);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDecidePolicyForNavigationResponse(
        IntPtr self, IntPtr sel, IntPtr webView, IntPtr navigationResponse, IntPtr decisionHandler)
    {
        WKDownloadInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self);
        long policy = PolicyAllow;

        if (interceptor is not null && !Libobjc.Bool_msgSend(navigationResponse, CanShowMimeTypeSel))
        {
            IntPtr response = Libobjc.IntPtr_msgSend(navigationResponse, ResponseSel);
            IntPtr urlHandle = Libobjc.IntPtr_msgSend(response, UrlSel);
            IntPtr absoluteStringHandle = Libobjc.IntPtr_msgSend(urlHandle, AbsoluteStringSel);
            if (Uri.TryCreate(NativeString.Read(absoluteStringHandle), UriKind.Absolute, out Uri? uri))
            {
                interceptor._pendingUri = uri;
                policy = PolicyDownload;
            }
        }

        var callback = (delegate* unmanaged[Cdecl]<IntPtr, long, void>)BlockLiteral.GetCallback(decisionHandler);
        callback(decisionHandler, policy);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDidBecomeDownload(IntPtr self, IntPtr sel, IntPtr webView, IntPtr navigationResponse, IntPtr download)
    {
        WKDownloadInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self);
        if (interceptor is null)
        {
            return;
        }

        interceptor._activeDownloadHandle = download;
        Libobjc.Void_msgSend(download, SetDelegateSel, self);

        BrowserTabViewModel? viewModel = interceptor._getViewModel();
        Uri? uri = interceptor._pendingUri;
        if (viewModel is null || uri is null)
        {
            return;
        }

        string fileName = uri.Segments.LastOrDefault() ?? uri.Host;
        Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadStarted(fileName));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDecideDestination(
        IntPtr self, IntPtr sel, IntPtr download, IntPtr response, IntPtr suggestedFilename, IntPtr completionHandler)
    {
        WKDownloadInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self);
        IntPtr destinationUrl = IntPtr.Zero;

        if (interceptor?._pendingUri is { } uri && interceptor._getViewModel() is { } viewModel)
        {
            string path = viewModel.GetBrowserDownloadPath(uri, NativeString.Read(suggestedFilename));
            interceptor._pendingFilePath = path;
            IntPtr pathString = NativeString.Create(path);
            destinationUrl = Libobjc.IntPtr_msgSend(NsUrlClass, FileUrlWithPathSel, pathString);
        }

        var callback = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)BlockLiteral.GetCallback(completionHandler);
        callback(completionHandler, destinationUrl);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDownloadDidFinish(IntPtr self, IntPtr sel, IntPtr download)
    {
        WKDownloadInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self);
        if (interceptor is null)
        {
            return;
        }

        BrowserTabViewModel? viewModel = interceptor._getViewModel();
        interceptor._activeDownloadHandle = IntPtr.Zero;
        interceptor._pendingUri = null;
        string? filePath = interceptor._pendingFilePath;
        interceptor._pendingFilePath = null;
        if (viewModel is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadUpdated(1, completed: true, canceled: false, path: filePath));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDownloadDidFail(IntPtr self, IntPtr sel, IntPtr download, IntPtr error, IntPtr resumeData)
    {
        WKDownloadInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self);
        if (interceptor is null)
        {
            return;
        }

        BrowserTabViewModel? viewModel = interceptor._getViewModel();
        bool wasCanceled = interceptor._cancelRequested;
        interceptor._activeDownloadHandle = IntPtr.Zero;
        interceptor._pendingUri = null;
        interceptor._pendingFilePath = null;
        interceptor._cancelRequested = false;
        if (viewModel is null)
        {
            return;
        }

        if (wasCanceled)
        {
            Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadUpdated(0, completed: false, canceled: true, path: null));
        }
        else
        {
            Dispatcher.UIThread.Post(() => viewModel.OnBrowserDownloadFailed("Download failed."));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr OnForwardingTargetForSelector(IntPtr self, IntPtr sel, IntPtr aSelector)
    {
        return ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self)?._originalNavigationDelegate ?? IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnRespondsToSelector(IntPtr self, IntPtr sel, IntPtr aSelector)
    {
        if (Libobjc.class_getInstanceMethod(s_class, aSelector) != IntPtr.Zero)
        {
            return 1;
        }

        IntPtr original = ManagedObjcClass.ReadManagedSelf<WKDownloadInterceptor>(self)?._originalNavigationDelegate ?? IntPtr.Zero;
        return original != IntPtr.Zero && Libobjc.Bool_msgSend(original, RespondsToSelectorSel, aSelector) ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCancelCompleted(IntPtr block, IntPtr resumeData)
    {
        // No-op; download:didFailWithError:resumeData: drives the state transition.
    }
}
