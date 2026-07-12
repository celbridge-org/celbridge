using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Installs an AppKit local key-down monitor for the keys that act on a focused document on the Skia head,
/// where a WKWebView is first responder and neither Uno's managed input nor the web content reliably sees the
/// event. A local monitor sees each key before it is dispatched to any responder. While a document holds focus
/// it keeps Tab inside the editor (indent, move a cell) instead of letting the AppKit key-view loop advance out
/// to the surrounding panels, and it routes Command+W and Command+Shift+W to the close-document shortcuts (which
/// WKWebView otherwise swallows as reserved key equivalents). In every case it swallows the key so it cannot
/// reach the surrounding app UI. macOS-only.
/// </summary>
internal static class MacOSKeyEventMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // BLOCK_IS_GLOBAL marks a block literal as a global (never copied or freed) block.
    private const int BlockIsGlobal = 1 << 28;

    // NSEventMaskKeyDown == 1 << 10.
    private const ulong EventMaskKeyDown = 1UL << 10;

    // kVK_Tab hardware key code.
    private const ulong TabKeyCode = 48;

    // NSEventModifierFlag bit positions.
    private const ulong ModifierFlagShift = 1UL << 17;
    private const ulong ModifierFlagControl = 1UL << 18;
    private const ulong ModifierFlagOption = 1UL << 19;
    private const ulong ModifierFlagCommand = 1UL << 20;

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
    private static IMessengerService? _messengerService;
    private static ILogger? _logger;

    public static void Start(IFocusService focusService, IMessengerService messengerService, ILogger logger)
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
        _messengerService = messengerService;
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
            var modifierFlags = SendMessageReturnNuint(nsEvent, GetSelector("modifierFlags"));

            bool isTab = keyCode == TabKeyCode;
            bool isCommand = (modifierFlags & ModifierFlagCommand) != 0;

            // Pass through anything that is neither Tab nor a Command chord before touching focus.
            if (!isTab
                && !isCommand)
            {
                return nsEvent;
            }

            // Only act while a document is focused. Tab still navigates the managed panels (Explorer, Inspector,
            // and so on) everywhere else, and the close shortcuts must not close a hidden document from another
            // panel.
            if (_focusService?.FocusedPanel != WorkspacePanel.Documents)
            {
                return nsEvent;
            }

            var shift = (modifierFlags & ModifierFlagShift) != 0;

            if (isTab)
            {
                // Let the focused editor act on Tab (a code editor indents or outdents). Whether or not it does,
                // the key is swallowed below so focus can never leave the document for the surrounding app UI.
                _focusService.EditTarget?.TryHandleTabKey(shift);
                return IntPtr.Zero;
            }

            // Command+W closes the active document, Command+Shift+W closes its section. WKWebView reserves
            // Command+W and never delivers it to the web content, so this native monitor is the only reliable
            // delivery path on the Skia head.
            if (IsCloseShortcut(nsEvent, modifierFlags))
            {
                if (shift)
                {
                    _messengerService?.Send(new CloseAllDocumentsRequestedMessage());
                }
                else
                {
                    _messengerService?.Send(new CloseActiveDocumentRequestedMessage());
                }

                return IntPtr.Zero;
            }

            return nsEvent;
        }
        catch (Exception exception)
        {
            // A throw must never unwind back into AppKit, so pass the event through on failure.
            _logger?.LogError(exception, "The key event monitor callback failed");
            return nsEvent;
        }
    }

    // True for Command+W and Command+Shift+W. Matched on the character (layout-aware) rather than the hardware
    // key code, and rejected when Control or Option is also held so it does not fire on unrelated chords.
    private static bool IsCloseShortcut(IntPtr nsEvent, ulong modifierFlags)
    {
        bool command = (modifierFlags & ModifierFlagCommand) != 0;
        bool control = (modifierFlags & ModifierFlagControl) != 0;
        bool option = (modifierFlags & ModifierFlagOption) != 0;

        if (!command
            || control
            || option)
        {
            return false;
        }

        var characters = ReadNSString(SendMessage(nsEvent, GetSelector("charactersIgnoringModifiers")));

        return string.Equals(characters, "w", StringComparison.OrdinalIgnoreCase);
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
