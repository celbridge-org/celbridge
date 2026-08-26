using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Celbridge.WebHost;
using Celbridge.Workspace;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Installs an AppKit local key-down monitor for the keys that act on a focused document on the Skia head,
/// where a WKWebView is first responder and neither Uno's managed input nor the web content reliably sees the
/// event. A local monitor sees each key before it is dispatched to any responder. While a document holds focus
/// it keeps Tab inside the document instead of letting the managed focus loop advance out to the surrounding
/// panels, and it routes Command+W, Command+Shift+W, and Command+F to the close-document and find shortcuts
/// (which WKWebView otherwise swallows as key equivalents). Tab is routed through the web-view focus
/// registry, which knows the focused surface: its edit target acts on the key (indent, move a cell), or the
/// key is delivered to the page itself so it can move between its own form fields. macOS-only.
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
    private static IWebViewFocusRegistry? _webViewFocusRegistry;
    private static IMessengerService? _messengerService;
    private static ILogger? _logger;

    public static void Start(
        IFocusService focusService,
        IWebViewFocusRegistry webViewFocusRegistry,
        IMessengerService messengerService,
        ILogger logger)
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
        _webViewFocusRegistry = webViewFocusRegistry;
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

            // Only act while a document is focused. Tab still navigates the managed panels (Explorer, Search,
            // and so on) everywhere else, the close shortcuts must not close a hidden document from another
            // panel, and Command+F falls through to the Find menu item, which drives the same document.
            if (_focusService?.FocusedPanel != WorkspacePanelId.Documents)
            {
                return nsEvent;
            }

            var shift = (modifierFlags & ModifierFlagShift) != 0;

            if (isTab)
            {
                // The focus registry owns the focused surface, so it routes Tab: the surface's edit target
                // acts on it (a code editor indents, the spreadsheet moves the active cell), or the key is
                // delivered straight to the page so it can move between its own form fields. Swallowed in
                // both cases, so the managed focus loop cannot walk focus out of the document. With no
                // hosted surface focused, normal focus navigation proceeds.
                if (_webViewFocusRegistry?.TryHandleTabKey(shift, nsEvent) == true)
                {
                    return IntPtr.Zero;
                }

                return nsEvent;
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

            // Command+F opens the active document's find bar, as the Find menu item does. WKWebView hands the
            // key equivalent to the web content rather than to the menu, so a page that ignores it (an
            // external site in a .webview) leaves the shortcut dead. Swallowed only once a find has begun: a
            // document with no find of its own leaves the key to the page, where the editors run their own.
            if (IsFindShortcut(nsEvent, modifierFlags))
            {
                if (ActiveDocumentFind.GetActiveFindableDocument()?.TryBeginFind() == true)
                {
                    return IntPtr.Zero;
                }

                return nsEvent;
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

    // True for Command+W and Command+Shift+W.
    private static bool IsCloseShortcut(IntPtr nsEvent, ulong modifierFlags)
    {
        return IsCommandShortcut(nsEvent, modifierFlags, "w");
    }

    // True for Command+F. Command+Shift+F is not a find shortcut, so Shift is rejected.
    private static bool IsFindShortcut(IntPtr nsEvent, ulong modifierFlags)
    {
        if ((modifierFlags & ModifierFlagShift) != 0)
        {
            return false;
        }

        return IsCommandShortcut(nsEvent, modifierFlags, "f");
    }

    // True when the event is Command plus the given character. Matched on the character (layout-aware) rather
    // than the hardware key code, and rejected when Control or Option is also held so it does not fire on
    // unrelated chords.
    private static bool IsCommandShortcut(IntPtr nsEvent, ulong modifierFlags, string character)
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

        return string.Equals(characters, character, StringComparison.OrdinalIgnoreCase);
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
