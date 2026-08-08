using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModManager.Ui.Services.Browser.Mac.Interop;

// Creates a native Objective-C class backed by a managed instance, using the
// same "ivar holds a GCHandle back-pointer" technique Avalonia.Controls.WebView
// uses internally (NSManagedObjectBase in their Macios interop). Lets our
// [UnmanagedCallersOnly] static callbacks (which only receive the native `self`
// pointer) recover the owning C# object.
[SupportedOSPlatform("macos")]
internal static unsafe class ManagedObjcClass
{
    private const string ManagedSelfIvarName = "_managedSelf";

    public static bool AddManagedSelfIvar(IntPtr classHandle) =>
        Libobjc.class_addIvar(classHandle, ManagedSelfIvarName, IntPtr.Size, 0, "^v") == 1;

    public static void WriteManagedSelf(IntPtr instanceHandle, object managedInstance)
    {
        GCHandle handle = GCHandle.Alloc(managedInstance, GCHandleType.Weak);
        SetIvarValue(instanceHandle, GCHandle.ToIntPtr(handle));
    }

    public static T? ReadManagedSelf<T>(IntPtr instanceHandle) where T : class
    {
        IntPtr handlePtr = GetIvarValue(instanceHandle);
        return handlePtr == IntPtr.Zero ? null : GCHandle.FromIntPtr(handlePtr).Target as T;
    }

    public static void FreeManagedSelf(IntPtr instanceHandle)
    {
        IntPtr handlePtr = GetIvarValue(instanceHandle);
        if (handlePtr != IntPtr.Zero)
        {
            GCHandle.FromIntPtr(handlePtr).Free();
        }
    }

    private static void SetIvarValue(IntPtr instanceHandle, IntPtr value)
    {
        IntPtr fieldPtr = GetIvarFieldPointer(instanceHandle);
        if (fieldPtr != IntPtr.Zero)
        {
            *(IntPtr*)fieldPtr = value;
        }
    }

    private static IntPtr GetIvarValue(IntPtr instanceHandle)
    {
        IntPtr fieldPtr = GetIvarFieldPointer(instanceHandle);
        return fieldPtr == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)fieldPtr;
    }

    private static IntPtr GetIvarFieldPointer(IntPtr instanceHandle)
    {
        IntPtr ivar = Libobjc.class_getInstanceVariable(Libobjc.object_getClass(instanceHandle), ManagedSelfIvarName);
        return ivar == IntPtr.Zero ? IntPtr.Zero : instanceHandle + Libobjc.ivar_getOffset(ivar);
    }
}
