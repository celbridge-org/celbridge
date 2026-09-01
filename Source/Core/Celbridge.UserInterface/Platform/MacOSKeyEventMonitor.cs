using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.WebHost;
using Celbridge.Workspace;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Installs an AppKit local key-down monitor for the keys that act on a focused document on the Skia head,
/// where a WKWebView is first responder and neither Uno's managed input nor the web content reliably sees the
/// event. A local monitor sees each key before it is dispatched to any responder. macOS-only.
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
            bool isCommand = (modifierFlags & MacOSKeyboardModifiers.CommandFlag) != 0;

            // Pass through anything that is neither Tab nor a Command chord before touching focus.
            if (!isTab
                && !isCommand)
            {
                return nsEvent;
            }

            // A Command chord arriving while a hosted web surface holds focus never reaches AppKit's
            // key-equivalent phase, so it is acted on here.
            if (isCommand
                && _webViewFocusRegistry?.HasFocusedSurface == true
                && TryHandleWebSurfaceCommandChord(nsEvent, keyCode, modifierFlags))
            {
                return IntPtr.Zero;
            }

            // Only act while a document is focused. Tab still navigates the managed panels (Explorer, Search,
            // and so on) everywhere else, the close shortcuts must not close a hidden document from another
            // panel, and Command+F falls through to the Find menu item, which drives the same document.
            if (_focusService?.FocusedPanel != FocusPanelId.Documents)
            {
                return nsEvent;
            }

            var shift = (modifierFlags & MacOSKeyboardModifiers.ShiftFlag) != 0;

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

            // The letter the chord names, which is what the shortcuts below are matched against.
            var shortcutCharacter = ResolveShortcutCharacter(nsEvent, keyCode);

            // Command+W closes the active document, Command+Shift+W closes its section. WKWebView reserves
            // Command+W and never delivers it to the web content, so this native monitor is the only reliable
            // delivery path on the Skia head.
            if (IsCloseShortcut(shortcutCharacter, modifierFlags))
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
            if (IsFindShortcut(shortcutCharacter, modifierFlags))
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

    // Acts on a Command chord while a hosted web surface holds focus. Uno's canvas is an NSTextInputClient,
    // so its window reports every key handled and AppKit's key-equivalent phase never runs. A verb the
    // focused surface can perform goes through the app's edit command, because the responder chain's
    // selectAll: moves WebKit's selection without telling the editor. Everything else goes to the menubar.
    // Returns whether the chord was acted on, in which case the key must not also reach the page.
    private static bool TryHandleWebSurfaceCommandChord(IntPtr nsEvent, ulong keyCode, ulong modifierFlags)
    {
        var editIntent = ResolveEditIntent(ResolveShortcutCharacter(nsEvent, keyCode), modifierFlags);
        if (editIntent is not null
            && TryHandleEditIntent(editIntent.Value))
        {
            return true;
        }

        return TryPerformMenuKeyEquivalent(nsEvent);
    }

    // Whether the focused surface answers for the verb: it can perform it, or it mediates the clipboard and
    // so an unavailable clipboard verb is swallowed rather than falling through to the responder chain.
    private static bool TryHandleEditIntent(EditIntent intent)
    {
        var editTarget = _focusService?.EditTarget;
        if (editTarget is null)
        {
            return false;
        }

        if (editTarget.CanPerformEdit(intent))
        {
            ServiceLocator.AcquireService<ICommandService>()
                .Execute<IPerformEditCommand>(command => command.Intent = intent);

            return true;
        }

        return editTarget.HostMediatedClipboard
            && intent is EditIntent.Cut or EditIntent.Copy or EditIntent.Paste;
    }

    // The edit verb a Command chord names, or null for a chord naming none.
    private static EditIntent? ResolveEditIntent(char? shortcutCharacter, ulong modifierFlags)
    {
        if (!IsPlainCommandChord(modifierFlags))
        {
            return null;
        }

        var shift = (modifierFlags & MacOSKeyboardModifiers.ShiftFlag) != 0;

        return MacOSEditShortcuts.ResolveIntent(shortcutCharacter, shift);
    }

    // Offers the chord to the menubar. Returns whether a menu item claimed and ran it.
    private static bool TryPerformMenuKeyEquivalent(IntPtr nsEvent)
    {
        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var mainMenu = SendMessage(application, GetSelector("mainMenu"));
        if (mainMenu == IntPtr.Zero)
        {
            return false;
        }

        return SendMessageReturnBool(mainMenu, GetSelector("performKeyEquivalent:"), nsEvent);
    }

    // True for Command+W and Command+Shift+W.
    private static bool IsCloseShortcut(char? shortcutCharacter, ulong modifierFlags)
    {
        return shortcutCharacter == 'w'
            && IsPlainCommandChord(modifierFlags);
    }

    // True for Command+F. Command+Shift+F is not a find shortcut, so Shift is rejected.
    private static bool IsFindShortcut(char? shortcutCharacter, ulong modifierFlags)
    {
        if ((modifierFlags & MacOSKeyboardModifiers.ShiftFlag) != 0)
        {
            return false;
        }

        return shortcutCharacter == 'f'
            && IsPlainCommandChord(modifierFlags);
    }

    // True for a Command chord holding neither Control nor Option, so a shortcut cannot fire on an unrelated
    // chord built over the same letter.
    private static bool IsPlainCommandChord(ulong modifierFlags)
    {
        bool command = (modifierFlags & MacOSKeyboardModifiers.CommandFlag) != 0;
        bool control = (modifierFlags & MacOSKeyboardModifiers.ControlFlag) != 0;
        bool option = (modifierFlags & MacOSKeyboardModifiers.OptionFlag) != 0;

        return command
            && !control
            && !option;
    }

    // The lower-case letter a chord names, or null when it names none. The event's own character answers for
    // a Latin layout whatever its arrangement, because it reports the key as labelled: the W key gives "w" on
    // AZERTY as on QWERTY, and a layout that swaps to QWERTY under Command (Dvorak-QWERTY) reports the
    // swapped letter. A non-Latin layout reports its own alphabet instead, so the key code is translated
    // through the ASCII-capable layout, which is how AppKit matches menu key equivalents.
    private static char? ResolveShortcutCharacter(IntPtr nsEvent, ulong keyCode)
    {
        var characters = ReadNSString(SendMessage(nsEvent, GetSelector("charactersIgnoringModifiers")));
        if (characters.Length == 1)
        {
            var character = char.ToLowerInvariant(characters[0]);
            if (character is >= 'a' and <= 'z')
            {
                return character;
            }
        }

        return MacOSKeyboardLayout.ResolveAsciiLetter(keyCode);
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
