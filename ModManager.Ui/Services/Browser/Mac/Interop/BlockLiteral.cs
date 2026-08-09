using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModManager.Ui.Services.Browser.Mac.Interop;

// Objective-C block ABI, needed to hand C# callbacks to APIs that take blocks
// (WKContentRuleListStore's compile completion, decidePolicyForNavigationResponse's
// decisionHandler, WKDownload's destination/cancel completion handlers). Struct
// layout and helpers are the well-known Apple block ABI, modeled directly on
// Avalonia.Controls.WebView's own BlockLiteral.cs for the same SDK target.
[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
internal unsafe ref struct BlockDescriptor
{
    public static IntPtr GlobalDescriptor { get; }

    static BlockDescriptor()
    {
        GlobalDescriptor = Marshal.AllocHGlobal(sizeof(BlockDescriptor));
        BlockDescriptor* descriptor = (BlockDescriptor*)(void*)GlobalDescriptor;
        descriptor->size = sizeof(BlockLiteral);
    }

    private long reserved;
    private long size;
    private delegate* unmanaged[Cdecl]<void*, void*, void> copyHelper;
    private delegate* unmanaged[Cdecl]<void*, void> disposeHelper;
}

[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
internal ref struct BlockLiteral(IntPtr invoke)
{
    private static IntPtr s_globalBlockClass;

    private static IntPtr NSConcreteGlobalBlock =>
        s_globalBlockClass != IntPtr.Zero
            ? s_globalBlockClass
            : s_globalBlockClass = Libobjc.dlsym(Libobjc.LinkLibSystem(), "_NSConcreteGlobalBlock");

    private IntPtr isa;
    private int flags;
    private int reserved;
    private IntPtr invoke = invoke;
    private IntPtr blockDescriptor = BlockDescriptor.GlobalDescriptor;
    private IntPtr state;

    public static unsafe IntPtr GetCallback(IntPtr blockPtr) => ((BlockLiteral*)(void*)blockPtr)->invoke;

    public static unsafe IntPtr TryGetBlockState(IntPtr blockPtr)
    {
        BlockLiteral* block = (BlockLiteral*)(void*)blockPtr;
        return block->blockDescriptor == BlockDescriptor.GlobalDescriptor ? block->state : default;
    }

    public static unsafe IntPtr GetBlockForFunctionPointer(IntPtr callback, IntPtr state)
    {
        BlockLiteral block = new(callback) { isa = NSConcreteGlobalBlock, state = state };
        return Libobjc._Block_copy(&block);
    }
}
