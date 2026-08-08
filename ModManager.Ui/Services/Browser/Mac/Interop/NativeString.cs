using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModManager.Ui.Services.Browser.Mac.Interop;

[SupportedOSPlatform("macos")]
internal static class NativeString
{
    private static readonly IntPtr NsStringClass = Libobjc.objc_getClass("NSString");
    private static readonly IntPtr AllocSel = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr InitWithUtf8Sel = Libobjc.sel_getUid("initWithUTF8String:");
    private static readonly IntPtr Utf8Sel = Libobjc.sel_getUid("UTF8String");

    // Returns a new, +1 retained NSString the caller owns (must release, or pass
    // ownership straight into an API that takes it).
    public static IntPtr Create(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            IntPtr instance = Libobjc.IntPtr_msgSend(NsStringClass, AllocSel);
            return Libobjc.IntPtr_msgSend(instance, InitWithUtf8Sel, utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static string? Read(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return null;
        }

        IntPtr utf8 = Libobjc.IntPtr_msgSend(nsString, Utf8Sel);
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }
}
