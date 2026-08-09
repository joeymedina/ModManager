using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModManager.Ui.Services.Browser.Mac.Interop;

// Minimal Objective-C runtime P/Invoke surface, modeled on the same technique
// Avalonia.Controls.WebView itself uses internally for macOS
// (Avalonia.Controls.WebView.Core/Macios/Interop/Libobjc.cs) — that package's own
// WKWebView wrapper is built this way because there is no first-party managed
// binding for WebKit outside the full Microsoft.macOS SDK/workload, which this
// repo does not otherwise need. This is a narrower subset: just enough to compile
// a WKContentRuleList and install a WKNavigationDelegate/WKDownloadDelegate proxy.
[SupportedOSPlatform("macos")]
internal static unsafe partial class Libobjc
{
    private const string ObjcLib = "/usr/lib/libobjc.dylib";
    private const string SystemLib = "/usr/lib/libSystem.dylib";
    private const string DlLib = "libdl.dylib";

    public static IntPtr LinkLibSystem() => dlopen(SystemLib, 0);

    [LibraryImport(DlLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr dlopen(string path, int mode);

    [LibraryImport(SystemLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr dlsym(IntPtr handle, string symbol);

    [LibraryImport(ObjcLib)]
    public static partial IntPtr _Block_copy(BlockLiteral* block);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getProtocol(string name);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_getUid(string selector);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [DllImport(ObjcLib)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [LibraryImport(ObjcLib)]
    public static partial int class_addProtocol(IntPtr cls, IntPtr protocol);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int class_addMethod(IntPtr cls, IntPtr selector, void* impl, string types);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int class_addIvar(IntPtr cls, string name, nint size, byte alignment, string types);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr class_getInstanceVariable(IntPtr cls, string name);

    [LibraryImport(ObjcLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

    [DllImport(ObjcLib)]
    public static extern IntPtr object_getClass(IntPtr obj);

    [DllImport(ObjcLib)]
    public static extern IntPtr ivar_getOffset(IntPtr ivar);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend(IntPtr receiver, IntPtr selector, long arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern int Int_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    public static extern int Int_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    public static bool Bool_msgSend(IntPtr receiver, IntPtr selector) => Int_msgSend(receiver, selector) != 0;
    public static bool Bool_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1) => Int_msgSend(receiver, selector, arg1) != 0;
}
