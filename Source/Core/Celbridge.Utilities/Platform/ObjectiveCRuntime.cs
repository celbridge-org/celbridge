using System.Runtime.InteropServices;
using System.Text;

namespace Celbridge.Utilities.Platform;

/// <summary>
/// The shared Objective-C runtime marshaling for the macOS native interop: class and selector lookup, the
/// objc_msgSend overloads keyed by argument and return shape, NSString conversion, blocks and AppKit local
/// event monitors. Only signatures shared by more than one consumer live here. File-specific interop
/// (struct-by-value returns, class-pair construction, the WebView reflection) stays with its consumer. The
/// DllImports are metadata until called, so the type compiles on every head. Call on the main (UI) thread,
/// where AppKit and WebKit are safe.
/// </summary>
public static class ObjectiveCRuntime
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // BLOCK_IS_GLOBAL marks a block literal as a global block, which is never copied or freed.
    private const int BlockIsGlobal = 1 << 28;

    private static readonly IntPtr RtldDefault = new(-2);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    // objc_msgSend for +addLocalMonitorForEventsMatchingMask:handler:, which takes an NSUInteger mask and a block.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageAddMonitor(IntPtr receiver, IntPtr selector, nuint mask, IntPtr block);

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    public static extern IntPtr GetSelector(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument, IntPtr thirdArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, double argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern nint SendMessageReturnNint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern nuint SendMessageReturnNuint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern double SendMessageReturnDouble(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendMessageReturnBool(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendMessageReturnBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "object_getClassName")]
    private static extern IntPtr object_getClassName(IntPtr target);

    /// <summary>
    /// Returns the Objective-C class name of a native object, for diagnostics when the runtime shape does not
    /// match expectations.
    /// </summary>
    public static string GetClassName(IntPtr target)
    {
        var classNamePointer = object_getClassName(target);
        return Marshal.PtrToStringAnsi(classNamePointer) ?? "(null)";
    }

    /// <summary>
    /// Builds an Objective-C block that calls a managed UnmanagedCallersOnly method. The method receives the block as
    /// its first argument. The block lives for the whole process, so build each one once and reuse it.
    /// </summary>
    public static IntPtr CreateGlobalBlock(IntPtr invoke)
    {
        var descriptor = new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>(),
        };
        var descriptorPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(descriptor, descriptorPointer, false);

        var block = new BlockLiteral
        {
            Isa = dlsym(RtldDefault, "_NSConcreteGlobalBlock"),
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = invoke,
            Descriptor = descriptorPointer,
        };
        var blockPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(block, blockPointer, false);

        return blockPointer;
    }

    /// <summary>
    /// Installs an AppKit local event monitor for the event types in the mask, for the life of the process. AppKit
    /// calls the handler with each matching event before dispatching the event to a window. The handler is an
    /// UnmanagedCallersOnly method that takes the block and the NSEvent. The handler's return value decides what
    /// AppKit dispatches: the original event, a replacement event, or nil to swallow the original.
    /// </summary>
    public static void AddLocalEventMonitor(ulong eventMask, IntPtr handler)
    {
        var block = CreateGlobalBlock(handler);
        var selector = GetSelector("addLocalMonitorForEventsMatchingMask:handler:");
        var monitor = SendMessageAddMonitor(GetClass("NSEvent"), selector, (nuint)eventMask, block);

        // Retain the monitor for the process lifetime so the subscription survives.
        SendMessage(monitor, GetSelector("retain"));
    }

    /// <summary>
    /// Creates an autoreleased NSString from a managed string via +[NSString stringWithUTF8String:], which
    /// copies the bytes, so the marshaling buffer is freed immediately after the call returns.
    /// </summary>
    public static IntPtr CreateNSString(string value)
    {
        var nsStringClass = GetClass("NSString");
        var selector = GetSelector("stringWithUTF8String:");

        var utf8Bytes = Encoding.UTF8.GetBytes(value + '\0');
        var buffer = Marshal.AllocHGlobal(utf8Bytes.Length);
        try
        {
            Marshal.Copy(utf8Bytes, 0, buffer, utf8Bytes.Length);
            return SendMessage(nsStringClass, selector, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Reads a managed string from an NSString via -[NSString UTF8String]. Returns the empty string for a
    /// null NSString.
    /// </summary>
    public static string ReadNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var utf8Pointer = SendMessage(nsString, GetSelector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8Pointer) ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }
}
