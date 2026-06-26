#if !WINDOWS
using System.Runtime.InteropServices;
using System.Text;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// One item in a native macOS menu. A Command item dispatches back to managed code by Tag; a Selector item
/// targets the AppKit responder chain by selector name (e.g. "copy:") and is auto-enabled only when some
/// responder handles it; a Separator is a divider.
/// </summary>
internal sealed record MacMenuItem
{
    public MacMenuItemKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public long Tag { get; init; }
    public string SelectorName { get; init; } = string.Empty;
    public string KeyEquivalent { get; init; } = string.Empty;
    public MacKeyModifier KeyModifiers { get; init; } = MacKeyModifier.Command;

    public static MacMenuItem Command(string title, long tag, string keyEquivalent = "", MacKeyModifier keyModifiers = MacKeyModifier.Command)
        => new() { Kind = MacMenuItemKind.Command, Title = title, Tag = tag, KeyEquivalent = keyEquivalent, KeyModifiers = keyModifiers };

    public static MacMenuItem Selector(string title, string selectorName, string keyEquivalent = "", MacKeyModifier keyModifiers = MacKeyModifier.Command)
        => new() { Kind = MacMenuItemKind.Selector, Title = title, SelectorName = selectorName, KeyEquivalent = keyEquivalent, KeyModifiers = keyModifiers };

    public static MacMenuItem Separator()
        => new() { Kind = MacMenuItemKind.Separator };
}

internal enum MacMenuItemKind
{
    Command,
    Selector,
    Separator
}

/// <summary>
/// Modifier keys for a menu item's keyboard shortcut. A shortcut carries Command by default; combine flags
/// for the less common chords (e.g. Hide Others is Option+Command).
/// </summary>
[Flags]
internal enum MacKeyModifier
{
    Command = 1,
    Shift = 2,
    Option = 4,
    Control = 8
}

/// <summary>
/// One top-level menu in the menubar. IsWindowMenu marks the menu AppKit manages as the standard Window
/// menu (it appends the open-window list and the standard items).
/// </summary>
internal sealed record MacMenu
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<MacMenuItem> Items { get; init; } = Array.Empty<MacMenuItem>();
    public bool IsWindowMenu { get; init; }
}

/// <summary>
/// A clickable link shown in the About panel's credits area. The URL opens in the default browser.
/// </summary>
internal sealed record MacAboutLink(string Label, string Url);

/// <summary>
/// Builds and installs a native AppKit menubar (NSMenu) behind Uno's macOS Skia head, which provides only a
/// minimal default app menu. Command items dispatch back to managed code through a single callback keyed by
/// the item's tag; selector items ride the responder chain. macOS-only; callers gate on
/// OperatingSystem.IsMacOS() and must invoke on the main (UI) thread, where AppKit is safe.
/// </summary>
internal static class MacOSMenuInterop
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // The custom NSObject subclass that receives every Command item's action. Created once per process; the
    // instance is never released so it stays alive as each NSMenuItem's (unretained) target.
    private static IntPtr _commandTarget;
    private static IntPtr _commandActionSelector;

    // Hold the callback function pointers alive for the process lifetime (they back native IMPs).
    private static MenuActionDelegate? _menuActionDelegate;
    private static MenuValidateDelegate? _menuValidateDelegate;

    // Invoked with the tag of the Command item the user chose.
    private static Action<long>? _onCommand;

    // Invoked with a Command item's tag to decide whether it is currently enabled.
    private static Func<long, bool>? _onValidate;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuActionDelegate(IntPtr self, IntPtr selector, IntPtr sender);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MenuValidateDelegate(IntPtr self, IntPtr selector, IntPtr menuItem);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(LibObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessagePtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage2Ptr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage3Ptr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidNint(IntPtr receiver, IntPtr selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidNuint(IntPtr receiver, IntPtr selector, nuint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendMessageReturnNint(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Builds the menubar from the given menus and installs it as the application's main menu. The first
    /// menu is the application menu (its title is replaced by the app name). onCommand runs a chosen Command
    /// item by tag; onValidate decides, by tag, whether a Command item is currently enabled. Returns false
    /// off macOS or if the AppKit application cannot be reached.
    /// </summary>
    public static bool Install(IReadOnlyList<MacMenu> menus, Action<long> onCommand, Func<long, bool> onValidate)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var applicationClass = objc_getClass("NSApplication");
        if (applicationClass == IntPtr.Zero)
        {
            return false;
        }

        var application = SendMessage(applicationClass, sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return false;
        }

        _onCommand = onCommand;
        _onValidate = onValidate;
        EnsureCommandTarget();

        var mainMenu = CreateMenu("MainMenu");
        IntPtr windowMenu = IntPtr.Zero;

        foreach (var menu in menus)
        {
            var submenu = CreateMenu(menu.Title);
            foreach (var item in menu.Items)
            {
                AddItem(submenu, item);
            }

            // A top-level menu is a titled item on the main menu whose submenu holds the entries.
            var topItem = CreateMenuItem(menu.Title, IntPtr.Zero, string.Empty);
            SendMessagePtr(topItem, sel_registerName("setSubmenu:"), submenu);
            SendMessagePtr(mainMenu, sel_registerName("addItem:"), topItem);

            if (menu.IsWindowMenu)
            {
                windowMenu = submenu;
            }
        }

        SendMessagePtr(application, sel_registerName("setMainMenu:"), mainMenu);
        if (windowMenu != IntPtr.Zero)
        {
            SendMessagePtr(application, sel_registerName("setWindowsMenu:"), windowMenu);
        }

        return true;
    }

    /// <summary>
    /// Shows the standard macOS About panel. The app icon, name, version, and copyright come from the
    /// bundle (Info.plist); the supplied links are added to the panel's credits area as clickable links.
    /// </summary>
    public static void ShowAboutPanel(IReadOnlyList<MacAboutLink> links)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var application = SendMessage(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return;
        }

        var credits = SendMessage(
            SendMessage(objc_getClass("NSMutableAttributedString"), sel_registerName("alloc")),
            sel_registerName("init"));

        var linkAttributeKey = CreateNSString("NSLink");
        var appendSelector = sel_registerName("appendAttributedString:");

        for (var index = 0; index < links.Count; index++)
        {
            if (index > 0)
            {
                SendMessagePtr(credits, appendSelector, CreatePlainAttributedString("\n"));
            }

            SendMessagePtr(credits, appendSelector, CreateLinkAttributedString(links[index], linkAttributeKey));
        }

        // Options: the credits links ("Credits"), plus an empty "Version" (NSAboutPanelOptionVersion) to
        // suppress the build-number parenthetical the panel appends after the version. Icon, name, and
        // version still come from the bundle.
        var options = SendMessage(
            SendMessage(objc_getClass("NSMutableDictionary"), sel_registerName("alloc")),
            sel_registerName("init"));
        var setObjectForKey = sel_registerName("setObject:forKey:");
        SendMessage2Ptr(options, setObjectForKey, credits, CreateNSString("Credits"));
        SendMessage2Ptr(options, setObjectForKey, CreateNSString(string.Empty), CreateNSString("Version"));

        SendMessagePtr(application, sel_registerName("orderFrontStandardAboutPanelWithOptions:"), options);
    }

    private static IntPtr CreatePlainAttributedString(string text)
    {
        var allocated = SendMessage(objc_getClass("NSAttributedString"), sel_registerName("alloc"));
        return SendMessagePtr(allocated, sel_registerName("initWithString:"), CreateNSString(text));
    }

    private static IntPtr CreateLinkAttributedString(MacAboutLink link, IntPtr linkAttributeKey)
    {
        var url = SendMessagePtr(objc_getClass("NSURL"), sel_registerName("URLWithString:"), CreateNSString(link.Url));
        var attributes = SendMessage2Ptr(
            objc_getClass("NSDictionary"),
            sel_registerName("dictionaryWithObject:forKey:"),
            url,
            linkAttributeKey);

        var allocated = SendMessage(objc_getClass("NSAttributedString"), sel_registerName("alloc"));
        return SendMessage2Ptr(allocated, sel_registerName("initWithString:attributes:"), CreateNSString(link.Label), attributes);
    }

    private static void EnsureCommandTarget()
    {
        if (_commandTarget != IntPtr.Zero)
        {
            return;
        }

        _commandActionSelector = sel_registerName("celbridgeMenuAction:");

        var newClass = objc_allocateClassPair(objc_getClass("NSObject"), "CelbridgeMenuTarget", 0);
        if (newClass != IntPtr.Zero)
        {
            _menuActionDelegate = HandleMenuAction;
            var actionImplementation = Marshal.GetFunctionPointerForDelegate(_menuActionDelegate);
            // "v@:@" = void return; arguments self (id), _cmd (SEL), sender (id).
            class_addMethod(newClass, _commandActionSelector, actionImplementation, "v@:@");

            _menuValidateDelegate = HandleValidateMenuItem;
            var validateImplementation = Marshal.GetFunctionPointerForDelegate(_menuValidateDelegate);
            // "c@:@" = BOOL (signed char) return; arguments self (id), _cmd (SEL), menuItem (id). AppKit
            // sends this to a Command item's target before the menu shows, to set the item's enabled state.
            class_addMethod(newClass, sel_registerName("validateMenuItem:"), validateImplementation, "c@:@");

            objc_registerClassPair(newClass);
        }
        else
        {
            // The class already exists (Install ran before in this process); reuse the registered one.
            newClass = objc_getClass("CelbridgeMenuTarget");
        }

        var allocated = SendMessage(newClass, sel_registerName("alloc"));
        _commandTarget = SendMessage(allocated, sel_registerName("init"));
    }

    private static void HandleMenuAction(IntPtr self, IntPtr selector, IntPtr sender)
    {
        // Runs on the AppKit main thread (the UI thread). Never let an exception cross back into native code.
        try
        {
            var tag = SendMessageReturnNint(sender, sel_registerName("tag"));
            _onCommand?.Invoke(tag);
        }
        catch
        {
            // Swallow: a throw here would unwind through AppKit and crash the process.
        }
    }

    private static byte HandleValidateMenuItem(IntPtr self, IntPtr selector, IntPtr menuItem)
    {
        // Runs on the AppKit main thread before a menu shows (and before a key equivalent fires). Defaults
        // to enabled on any failure; never let an exception cross back into native code.
        try
        {
            var tag = SendMessageReturnNint(menuItem, sel_registerName("tag"));
            var enabled = _onValidate?.Invoke(tag) ?? true;
            return enabled ? (byte)1 : (byte)0;
        }
        catch
        {
            return 1;
        }
    }

    private static void AddItem(IntPtr menu, MacMenuItem item)
    {
        if (item.Kind == MacMenuItemKind.Separator)
        {
            var separator = SendMessage(objc_getClass("NSMenuItem"), sel_registerName("separatorItem"));
            SendMessagePtr(menu, sel_registerName("addItem:"), separator);
            return;
        }

        var action = item.Kind == MacMenuItemKind.Command
            ? _commandActionSelector
            : sel_registerName(item.SelectorName);

        var menuItem = CreateMenuItem(item.Title, action, item.KeyEquivalent);

        if (item.Kind == MacMenuItemKind.Command)
        {
            SendMessagePtr(menuItem, sel_registerName("setTarget:"), _commandTarget);
            SendMessageVoidNint(menuItem, sel_registerName("setTag:"), (nint)item.Tag);
        }

        // A shortcut carries Command by default; override the mask only for other chords (e.g. Hide Others
        // is Option+Command+H, which must differ from Hide's Command+H).
        if (item.KeyEquivalent.Length > 0
            && item.KeyModifiers != MacKeyModifier.Command)
        {
            SendMessageVoidNuint(menuItem, sel_registerName("setKeyEquivalentModifierMask:"), ToModifierFlags(item.KeyModifiers));
        }

        SendMessagePtr(menu, sel_registerName("addItem:"), menuItem);
    }

    private static nuint ToModifierFlags(MacKeyModifier modifiers)
    {
        // NSEventModifierFlags bit positions.
        nuint flags = 0;
        if (modifiers.HasFlag(MacKeyModifier.Shift))
        {
            flags |= (nuint)(1 << 17);
        }
        if (modifiers.HasFlag(MacKeyModifier.Control))
        {
            flags |= (nuint)(1 << 18);
        }
        if (modifiers.HasFlag(MacKeyModifier.Option))
        {
            flags |= (nuint)(1 << 19);
        }
        if (modifiers.HasFlag(MacKeyModifier.Command))
        {
            flags |= (nuint)(1 << 20);
        }

        return flags;
    }

    private static IntPtr CreateMenu(string title)
    {
        var allocated = SendMessage(objc_getClass("NSMenu"), sel_registerName("alloc"));
        return SendMessagePtr(allocated, sel_registerName("initWithTitle:"), CreateNSString(title));
    }

    private static IntPtr CreateMenuItem(string title, IntPtr action, string keyEquivalent)
    {
        var allocated = SendMessage(objc_getClass("NSMenuItem"), sel_registerName("alloc"));
        return SendMessage3Ptr(
            allocated,
            sel_registerName("initWithTitle:action:keyEquivalent:"),
            CreateNSString(title),
            action,
            CreateNSString(keyEquivalent));
    }

    private static IntPtr CreateNSString(string value)
    {
        // stringWithUTF8String: copies the bytes into a new (autoreleased) NSString, so the pinned buffer
        // can be freed immediately after the call returns.
        var utf8 = Encoding.UTF8.GetBytes(value);
        var buffer = new byte[utf8.Length + 1];
        Array.Copy(utf8, buffer, utf8.Length);

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return SendMessagePtr(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}
#endif
