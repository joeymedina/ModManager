using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using ModManager.Ui.Services.Browser.Mac.Interop;

namespace ModManager.Ui.Services.Browser.Mac;

// WKWebView never creates a window for target="_blank" links or window.open() calls
// unless its uiDelegate implements createWebViewWithConfiguration:forNavigationAction:
// windowFeatures: — Avalonia's WKWebView wrapper implements no WKUIDelegate at all
// (no createWebView-related symbols anywhere in the assembly), so those links
// silently do nothing today, unlike the navigationDelegate slot WKDownloadInterceptor
// already overrides. This becomes the WKWebView's uiDelegate to implement that one
// selector, extract the target URL, and hand it to the app to open as a new tab
// instead of a native child window (returning nil tells WebKit not to create one).
//
// Forwards every other selector back to Avalonia's original uiDelegate (nil today,
// since Avalonia never sets one — kept for forward safety) via the same
// forwardingTargetForSelector:/respondsToSelector: pair WKDownloadInterceptor uses
// for its navigationDelegate slot.
[SupportedOSPlatform("macos")]
internal sealed unsafe class WKNewWindowInterceptor : IDisposable
{
    private static readonly IntPtr RequestSel = Libobjc.sel_getUid("request");
    private static readonly IntPtr UrlSel = Libobjc.sel_getUid("URL");
    private static readonly IntPtr AbsoluteStringSel = Libobjc.sel_getUid("absoluteString");
    private static readonly IntPtr UiDelegateSel = Libobjc.sel_getUid("UIDelegate");
    private static readonly IntPtr SetUiDelegateSel = Libobjc.sel_getUid("setUIDelegate:");
    private static readonly IntPtr AllocSel = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr InitSel = Libobjc.sel_getUid("init");
    private static readonly IntPtr RetainSel = Libobjc.sel_getUid("retain");
    private static readonly IntPtr ReleaseSel = Libobjc.sel_getUid("release");
    private static readonly IntPtr RespondsToSelectorSel = Libobjc.sel_getUid("respondsToSelector:");

    private static readonly IntPtr s_class;

    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>
        s_createWebView = &OnCreateWebView;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>
        s_forwardingTargetForSelector = &OnForwardingTargetForSelector;
    private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>
        s_respondsToSelector = &OnRespondsToSelector;

    static WKNewWindowInterceptor()
    {
        IntPtr cls = Libobjc.objc_allocateClassPair(
            Libobjc.objc_getClass("NSObject"),
            "ModManagerWKNewWindowInterceptor",
            0);

        AddProtocol(cls, "WKUIDelegate");

        AddMethod(cls, "webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:", s_createWebView, "@@:@@@@");
        AddMethod(cls, "forwardingTargetForSelector:", s_forwardingTargetForSelector, "@@::");
        AddMethod(cls, "respondsToSelector:", s_respondsToSelector, "B@::");

        bool ivarAdded = ManagedObjcClass.AddManagedSelfIvar(cls);
        Debug.Assert(ivarAdded);

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    private readonly Action<Uri> _onNewTabRequested;
    private readonly IntPtr _webViewHandle;
    private readonly IntPtr _originalUiDelegate;

    public WKNewWindowInterceptor(IntPtr webViewHandle, Action<Uri> onNewTabRequested)
    {
        _onNewTabRequested = onNewTabRequested;
        _webViewHandle = webViewHandle;
        _originalUiDelegate = Libobjc.IntPtr_msgSend(webViewHandle, UiDelegateSel);

        IntPtr allocated = Libobjc.IntPtr_msgSend(s_class, AllocSel);
        Handle = Libobjc.IntPtr_msgSend(allocated, InitSel);
        Libobjc.IntPtr_msgSend(Handle, RetainSel);
        ManagedObjcClass.WriteManagedSelf(Handle, this);

        Libobjc.Void_msgSend(webViewHandle, SetUiDelegateSel, Handle);
    }

    public IntPtr Handle { get; }

    public void Dispose()
    {
        Libobjc.Void_msgSend(_webViewHandle, SetUiDelegateSel, IntPtr.Zero);
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
    private static IntPtr OnCreateWebView(
        IntPtr self, IntPtr sel, IntPtr webView, IntPtr configuration, IntPtr navigationAction, IntPtr windowFeatures)
    {
        WKNewWindowInterceptor? interceptor = ManagedObjcClass.ReadManagedSelf<WKNewWindowInterceptor>(self);
        if (interceptor is null)
        {
            return IntPtr.Zero;
        }

        IntPtr request = Libobjc.IntPtr_msgSend(navigationAction, RequestSel);
        IntPtr urlHandle = Libobjc.IntPtr_msgSend(request, UrlSel);
        IntPtr absoluteStringHandle = Libobjc.IntPtr_msgSend(urlHandle, AbsoluteStringSel);
        if (Uri.TryCreate(NativeString.Read(absoluteStringHandle), UriKind.Absolute, out Uri? uri))
        {
            Action<Uri> onNewTabRequested = interceptor._onNewTabRequested;
            Dispatcher.UIThread.Post(() => onNewTabRequested(uri));
        }

        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr OnForwardingTargetForSelector(IntPtr self, IntPtr sel, IntPtr aSelector)
    {
        return ManagedObjcClass.ReadManagedSelf<WKNewWindowInterceptor>(self)?._originalUiDelegate ?? IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnRespondsToSelector(IntPtr self, IntPtr sel, IntPtr aSelector)
    {
        if (Libobjc.class_getInstanceMethod(s_class, aSelector) != IntPtr.Zero)
        {
            return 1;
        }

        IntPtr original = ManagedObjcClass.ReadManagedSelf<WKNewWindowInterceptor>(self)?._originalUiDelegate ?? IntPtr.Zero;
        return original != IntPtr.Zero && Libobjc.Bool_msgSend(original, RespondsToSelectorSel, aSelector) ? (byte)1 : (byte)0;
    }
}
