using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Celbridge.Workspace;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Installs an AppKit local key-down monitor that keeps Tab inside a focused document instead of letting it
/// move focus out to the surrounding panels. On the Skia head a Tab press routes either natively (the WKWebView
/// is first responder) or through Uno's focus manager, and in both cases the AppKit key-view loop advances out
/// of the WebView regardless of what the web content does with the key. A local monitor sees the event before
/// it is dispatched to any responder, so while a document holds focus it hands Tab to the focused editor (which
/// indents, moves a cell, and so on) and swallows the key so focus cannot leave the document. macOS-only.
/// </summary>
internal static class MacOSTabKeyMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // BLOCK_IS_GLOBAL marks a block literal as a global (never copied or freed) block.
    private const int BlockIsGlobal = 1 << 28;

    // NSEventMaskKeyDown == 1 << 10.
    private const ulong EventMaskKeyDown = 1UL << 10;

    // kVK_Tab hardware key code.
    private const ulong TabKeyCode = 48;

    // NSEventModifierFlagShift == 1 << 17.
    private const ulong ModifierFlagShift = 1UL << 17;

    // objc_msgSend for +addLocalMonitorForEventsMatchingMask:handler: (an NSUInteger mask then a block).
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageAddMonitor(IntPtr receiver, IntPtr selector, nuint mask, IntPtr block);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static readonly IntPtr RtldDefault = new(-2);

    private static bool _started;
    private static IntPtr _monitor;
    private static IntPtr _monitorBlock;
    private static IFocusService? _focusService;
    private static ILogger? _logger;

    public static void Start(IFocusService focusService, ILogger logger)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (_started)
        {
            return;
        }

        _started = true;
        _focusService = focusService;
        _logger = logger;

        var nsEventClass = GetClass("NSEvent");
        var selector = GetSelector("addLocalMonitorForEventsMatchingMask:handler:");
        var block = EnsureMonitorBlock();

        var monitor = SendMessageAddMonitor(nsEventClass, selector, (nuint)EventMaskKeyDown, block);

        // Retain the monitor object for the process lifetime so the subscription survives.
        _monitor = SendMessage(monitor, GetSelector("retain"));
    }

    // The handler is an Objective-C block of shape NSEvent* (^)(NSEvent*). Built once as a no-capture global
    // block whose invoke pointer is a managed method: returning the event passes it on, returning nil swallows
    // it.
    private static unsafe IntPtr EnsureMonitorBlock()
    {
        if (_monitorBlock != IntPtr.Zero)
        {
            return _monitorBlock;
        }

        var descriptor = new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>(),
        };
        var descriptorPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(descriptor, descriptorPointer, false);

        var blockIsa = dlsym(RtldDefault, "_NSConcreteGlobalBlock");
        var invoke = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&MonitorCallback;

        var block = new BlockLiteral
        {
            Isa = blockIsa,
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = invoke,
            Descriptor = descriptorPointer,
        };
        var blockPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(block, blockPointer, false);

        _monitorBlock = blockPointer;
        return blockPointer;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr MonitorCallback(IntPtr block, IntPtr nsEvent)
    {
        // Runs on the main thread during event dispatch. Never let an exception cross back into AppKit.
        try
        {
            var keyCode = SendMessageReturnNuint(nsEvent, GetSelector("keyCode")) & 0xFFFF;
            if (keyCode != TabKeyCode)
            {
                return nsEvent;
            }

            // Only intercept Tab while a document is focused. Tab still navigates the managed panels
            // (Explorer, Inspector, and so on) everywhere else.
            if (_focusService?.FocusedPanel != WorkspacePanel.Documents)
            {
                return nsEvent;
            }

            var modifierFlags = SendMessageReturnNuint(nsEvent, GetSelector("modifierFlags"));
            var shift = (modifierFlags & ModifierFlagShift) != 0;

            // Let the focused editor act on Tab (a code editor indents or outdents). Whether or not it does,
            // the key is swallowed below so focus can never leave the document for the surrounding app UI.
            _focusService.EditTarget?.TryHandleTabKey(shift);

            return IntPtr.Zero;
        }
        catch (Exception exception)
        {
            // A throw must never unwind back into AppKit, so pass the event through on failure.
            _logger?.LogError(exception, "The Tab key monitor callback failed");
            return nsEvent;
        }
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
