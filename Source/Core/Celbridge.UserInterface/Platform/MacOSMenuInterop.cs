using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// One item in a native macOS menu. A Command item dispatches back to managed code by Tag. A Selector item
/// targets the AppKit responder chain by selector name (e.g. "copy:") and is auto-enabled only when some
/// responder handles it. A RoutedCommand item is a Command that falls back to a Selector: managed code takes
/// it when it answers for it, and otherwise the item behaves as the Selector item would. A Separator is a
/// divider. A Submenu item opens a nested menu whose contents are rebuilt by SubmenuItemsProvider each time
/// it is shown, so a changing list (e.g. recent projects) stays current.
/// </summary>
internal sealed record MacMenuItem
{
    public MacMenuItemKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public long Tag { get; init; }
    public string SelectorName { get; init; } = string.Empty;
    public string KeyEquivalent { get; init; } = string.Empty;
    public MacKeyModifier KeyModifiers { get; init; } = MacKeyModifier.Command;
    public string FallbackSelectorName { get; init; } = string.Empty;
    public Func<IReadOnlyList<MacMenuItem>>? SubmenuItemsProvider { get; init; }

    public static MacMenuItem Command(string title, long tag, string keyEquivalent = "", MacKeyModifier keyModifiers = MacKeyModifier.Command)
        => new() { Kind = MacMenuItemKind.Command, Title = title, Tag = tag, KeyEquivalent = keyEquivalent, KeyModifiers = keyModifiers };

    public static MacMenuItem Selector(string title, string selectorName, string keyEquivalent = "", MacKeyModifier keyModifiers = MacKeyModifier.Command)
        => new() { Kind = MacMenuItemKind.Selector, Title = title, SelectorName = selectorName, KeyEquivalent = keyEquivalent, KeyModifiers = keyModifiers };

    public static MacMenuItem RoutedCommand(string title, long tag, string fallbackSelectorName, string keyEquivalent = "", MacKeyModifier keyModifiers = MacKeyModifier.Command)
        => new() { Kind = MacMenuItemKind.Command, Title = title, Tag = tag, FallbackSelectorName = fallbackSelectorName, KeyEquivalent = keyEquivalent, KeyModifiers = keyModifiers };

    public static MacMenuItem Submenu(string title, Func<IReadOnlyList<MacMenuItem>> itemsProvider)
        => new() { Kind = MacMenuItemKind.Submenu, Title = title, SubmenuItemsProvider = itemsProvider };

    public static MacMenuItem Separator()
        => new() { Kind = MacMenuItemKind.Separator };
}

internal enum MacMenuItemKind
{
    Command,
    Selector,
    Submenu,
    Separator
}

/// <summary>
/// Modifier keys for a menu item's keyboard shortcut. A shortcut carries Command by default. Combine flags
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
/// The display state of a Command item at the moment its menu is about to be shown: whether the item can
/// be chosen, and whether it carries a check mark. Selector items are validated by AppKit instead.
/// </summary>
internal sealed record MacMenuItemState(bool IsEnabled, bool IsChecked, bool DefersToResponderChain = false)
{
    public static readonly MacMenuItemState Enabled = new(true, false);

    public static readonly MacMenuItemState Disabled = new(false, false);

    /// <summary>
    /// A RoutedCommand item whose state is AppKit's to decide, because the verb belongs to the responder
    /// chain. Its fallback selector is validated there, as a Selector item's would be.
    /// </summary>
    public static readonly MacMenuItemState ResponderChain = new(false, false, DefersToResponderChain: true);

    /// <summary>
    /// An enabled item that shows a check mark while it represents the current selection.
    /// </summary>
    public static MacMenuItemState Checkable(bool isChecked) => new(true, isChecked);
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
/// the item's tag. Selector items ride the responder chain, and a RoutedCommand item does both: managed
/// state and dispatch, falling back to its selector on the chain. macOS-only. Callers gate on
/// OperatingSystem.IsMacOS() and must invoke on the main (UI) thread, where AppKit is safe.
/// </summary>
internal static class MacOSMenuInterop
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // The custom NSObject subclass that receives every Command item's action. Created once per process. The
    // instance is never released so it stays alive as each NSMenuItem's (unretained) target.
    private static IntPtr _commandTarget;
    private static IntPtr _commandActionSelector;

    // Hold the callback function pointers alive for the process lifetime (they back native IMPs).
    private static MenuActionDelegate? _menuActionDelegate;
    private static MenuValidateDelegate? _menuValidateDelegate;
    private static MenuNeedsUpdateDelegate? _menuNeedsUpdateDelegate;

    // Invoked with the tag of the Command item the user chose.
    private static Action<long>? _onCommand;

    // Invoked with a Command item's tag to decide how the item should currently be displayed.
    private static Func<long, MacMenuItemState>? _onQueryState;

    // Each dynamic submenu's NSMenu pointer mapped to the provider that rebuilds its items on open. AppKit
    // hands the NSMenu back in menuNeedsUpdate:, so the pointer is the lookup key.
    private static readonly Dictionary<IntPtr, Func<IReadOnlyList<MacMenuItem>>> _dynamicSubmenuProviders = new();

    // Each RoutedCommand item's tag mapped to the selector it falls back to, so a deferred item can be
    // validated against the responder chain the way AppKit validates a Selector item.
    private static readonly Dictionary<long, string> _fallbackSelectors = new();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuActionDelegate(IntPtr self, IntPtr selector, IntPtr sender);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MenuValidateDelegate(IntPtr self, IntPtr selector, IntPtr menuItem);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuNeedsUpdateDelegate(IntPtr self, IntPtr selector, IntPtr menu);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(LibObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    // -[NSApplication sendAction:to:from:], which returns whether a responder took the action.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendActionMessage(IntPtr receiver, IntPtr selector, IntPtr action, IntPtr target, IntPtr sender);

    /// <summary>
    /// Builds the menubar from the given menus and installs it as the application's main menu. The first
    /// menu is the application menu (its title is replaced by the app name). onCommand runs a chosen Command
    /// item by tag. onQueryState decides, by tag, how a Command item is currently displayed. Returns false
    /// off macOS or if the AppKit application cannot be reached.
    /// </summary>
    public static bool Install(IReadOnlyList<MacMenu> menus, Action<long> onCommand, Func<long, MacMenuItemState> onQueryState)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var applicationClass = GetClass("NSApplication");
        if (applicationClass == IntPtr.Zero)
        {
            return false;
        }

        var application = SendMessage(applicationClass, GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return false;
        }

        _onCommand = onCommand;
        _onQueryState = onQueryState;
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
            SendMessage(topItem, GetSelector("setSubmenu:"), submenu);
            SendMessage(mainMenu, GetSelector("addItem:"), topItem);

            if (menu.IsWindowMenu)
            {
                windowMenu = submenu;
            }
        }

        SendMessage(application, GetSelector("setMainMenu:"), mainMenu);
        if (windowMenu != IntPtr.Zero)
        {
            SendMessage(application, GetSelector("setWindowsMenu:"), windowMenu);
        }

        return true;
    }

    /// <summary>
    /// Shows the standard macOS About panel. The app icon, name, version, and copyright come from the
    /// bundle (Info.plist). The supplied links are added to the panel's credits area as clickable links.
    /// </summary>
    public static void ShowAboutPanel(IReadOnlyList<MacAboutLink> links)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return;
        }

        var credits = SendMessage(
            SendMessage(GetClass("NSMutableAttributedString"), GetSelector("alloc")),
            GetSelector("init"));

        var linkAttributeKey = CreateNSString("NSLink");
        var appendSelector = GetSelector("appendAttributedString:");

        for (var index = 0; index < links.Count; index++)
        {
            if (index > 0)
            {
                SendMessage(credits, appendSelector, CreatePlainAttributedString("\n"));
            }

            SendMessage(credits, appendSelector, CreateLinkAttributedString(links[index], linkAttributeKey));
        }

        // Options: the credits links ("Credits"), plus an empty "Version" (NSAboutPanelOptionVersion) to
        // suppress the build-number parenthetical the panel appends after the version. Icon, name, and
        // version still come from the bundle.
        var options = SendMessage(
            SendMessage(GetClass("NSMutableDictionary"), GetSelector("alloc")),
            GetSelector("init"));
        var setObjectForKey = GetSelector("setObject:forKey:");
        SendMessage(options, setObjectForKey, credits, CreateNSString("Credits"));
        SendMessage(options, setObjectForKey, CreateNSString(string.Empty), CreateNSString("Version"));

        SendMessage(application, GetSelector("orderFrontStandardAboutPanelWithOptions:"), options);
    }

    private static IntPtr CreatePlainAttributedString(string text)
    {
        var allocated = SendMessage(GetClass("NSAttributedString"), GetSelector("alloc"));
        return SendMessage(allocated, GetSelector("initWithString:"), CreateNSString(text));
    }

    private static IntPtr CreateLinkAttributedString(MacAboutLink link, IntPtr linkAttributeKey)
    {
        var url = SendMessage(GetClass("NSURL"), GetSelector("URLWithString:"), CreateNSString(link.Url));
        var attributes = SendMessage(
            GetClass("NSDictionary"),
            GetSelector("dictionaryWithObject:forKey:"),
            url,
            linkAttributeKey);

        var allocated = SendMessage(GetClass("NSAttributedString"), GetSelector("alloc"));
        return SendMessage(allocated, GetSelector("initWithString:attributes:"), CreateNSString(link.Label), attributes);
    }

    private static void EnsureCommandTarget()
    {
        if (_commandTarget != IntPtr.Zero)
        {
            return;
        }

        _commandActionSelector = GetSelector("celbridgeMenuAction:");

        var newClass = objc_allocateClassPair(GetClass("NSObject"), "CelbridgeMenuTarget", 0);
        if (newClass != IntPtr.Zero)
        {
            _menuActionDelegate = HandleMenuAction;
            var actionImplementation = Marshal.GetFunctionPointerForDelegate(_menuActionDelegate);
            // "v@:@" = void return. Arguments self (id), _cmd (SEL), sender (id).
            class_addMethod(newClass, _commandActionSelector, actionImplementation, "v@:@");

            _menuValidateDelegate = HandleValidateMenuItem;
            var validateImplementation = Marshal.GetFunctionPointerForDelegate(_menuValidateDelegate);
            // "c@:@" = BOOL (signed char) return. Arguments self (id), _cmd (SEL), menuItem (id). AppKit
            // sends this to a Command item's target before the menu shows, to set the item's enabled state.
            class_addMethod(newClass, GetSelector("validateMenuItem:"), validateImplementation, "c@:@");

            _menuNeedsUpdateDelegate = HandleMenuNeedsUpdate;
            var needsUpdateImplementation = Marshal.GetFunctionPointerForDelegate(_menuNeedsUpdateDelegate);
            // "v@:@" = void return. Arguments self (id), _cmd (SEL), menu (id). AppKit sends this to a dynamic
            // submenu's delegate (this same object) just before the submenu is displayed, so it can be rebuilt.
            class_addMethod(newClass, GetSelector("menuNeedsUpdate:"), needsUpdateImplementation, "v@:@");

            objc_registerClassPair(newClass);
        }
        else
        {
            // The class already exists (Install ran before in this process). Reuse the registered one.
            newClass = GetClass("CelbridgeMenuTarget");
        }

        var allocated = SendMessage(newClass, GetSelector("alloc"));
        _commandTarget = SendMessage(allocated, GetSelector("init"));
    }

    private static void HandleMenuAction(IntPtr self, IntPtr selector, IntPtr sender)
    {
        // Runs on the AppKit main thread (the UI thread). Never let an exception cross back into native code.
        try
        {
            var tag = SendMessageReturnNint(sender, GetSelector("tag"));
            _onCommand?.Invoke(tag);
        }
        catch
        {
            // Swallow: a throw here would unwind through AppKit and crash the process.
        }
    }

    private static byte HandleValidateMenuItem(IntPtr self, IntPtr selector, IntPtr menuItem)
    {
        // Runs on the AppKit main thread before a menu shows (and before a key equivalent fires). AppKit
        // takes the enabled state from the return value, and the check mark has to be written onto the item
        // here, as this is the only point at which it is known to be up to date. Defaults to enabled and
        // unchecked on any failure. Never let an exception cross back into native code.
        try
        {
            var tag = SendMessageReturnNint(menuItem, GetSelector("tag"));
            var state = _onQueryState?.Invoke(tag) ?? MacMenuItemState.Enabled;

            // NSControlStateValueOn is 1 and NSControlStateValueOff is 0.
            nint controlState = state.IsChecked ? 1 : 0;
            SendMessageVoid(menuItem, GetSelector("setState:"), controlState);

            var isEnabled = state.DefersToResponderChain
                ? ValidateFallbackAction(tag, menuItem)
                : state.IsEnabled;

            return isEnabled ? (byte)1 : (byte)0;
        }
        catch
        {
            return 1;
        }
    }

    private static void HandleMenuNeedsUpdate(IntPtr self, IntPtr selector, IntPtr menu)
    {
        // Runs on the AppKit main thread just before a dynamic submenu is displayed. Never let an exception
        // cross back into native code.
        try
        {
            if (_dynamicSubmenuProviders.TryGetValue(menu, out var provider))
            {
                PopulateDynamicSubmenu(menu, provider);
            }
        }
        catch
        {
            // Swallow: a throw here would unwind through AppKit and crash the process.
        }
    }

    /// <summary>
    /// Sends the named selector down the AppKit responder chain, as choosing a Selector item would. Returns
    /// whether a responder took it.
    /// </summary>
    public static bool SendActionToResponderChain(string selectorName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return false;
        }

        return SendActionMessage(
            application,
            GetSelector("sendAction:to:from:"),
            GetSelector(selectorName),
            IntPtr.Zero,
            IntPtr.Zero);
    }

    // Validates a deferred RoutedCommand item the way AppKit validates a Selector item: find the responder
    // that implements the action, then ask it. The item's own target is this object rather than that
    // responder, so AppKit's automatic validation never runs for it.
    private static bool ValidateFallbackAction(long tag, IntPtr menuItem)
    {
        if (!_fallbackSelectors.TryGetValue(tag, out var selectorName))
        {
            return false;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return false;
        }

        var target = SendMessage(application, GetSelector("targetForAction:"), GetSelector(selectorName));
        if (target == IntPtr.Zero)
        {
            return false;
        }

        var respondsToSelector = GetSelector("respondsToSelector:");

        var validateUserInterfaceItem = GetSelector("validateUserInterfaceItem:");
        if (SendMessageReturnBool(target, respondsToSelector, validateUserInterfaceItem))
        {
            return SendMessageReturnBool(target, validateUserInterfaceItem, menuItem);
        }

        var validateMenuItem = GetSelector("validateMenuItem:");
        if (SendMessageReturnBool(target, respondsToSelector, validateMenuItem))
        {
            return SendMessageReturnBool(target, validateMenuItem, menuItem);
        }

        // A responder that implements the action but validates nothing takes it, which is AppKit's default.
        return true;
    }

    private static void AddItem(IntPtr menu, MacMenuItem item)
    {
        if (item.Kind == MacMenuItemKind.Separator)
        {
            var separator = SendMessage(GetClass("NSMenuItem"), GetSelector("separatorItem"));
            SendMessage(menu, GetSelector("addItem:"), separator);
            return;
        }

        if (item.Kind == MacMenuItemKind.Submenu)
        {
            AddSubmenu(menu, item);
            return;
        }

        var action = item.Kind == MacMenuItemKind.Command
            ? _commandActionSelector
            : GetSelector(item.SelectorName);

        var menuItem = CreateMenuItem(item.Title, action, item.KeyEquivalent);

        if (item.Kind == MacMenuItemKind.Command)
        {
            SendMessage(menuItem, GetSelector("setTarget:"), _commandTarget);
            SendMessageVoid(menuItem, GetSelector("setTag:"), (nint)item.Tag);

            if (item.FallbackSelectorName.Length > 0)
            {
                _fallbackSelectors[item.Tag] = item.FallbackSelectorName;
            }
        }

        // A shortcut carries Command by default. Override the mask only for other chords (e.g. Hide Others
        // is Option+Command+H, which must differ from Hide's Command+H).
        if (item.KeyEquivalent.Length > 0
            && item.KeyModifiers != MacKeyModifier.Command)
        {
            SendMessageVoid(menuItem, GetSelector("setKeyEquivalentModifierMask:"), ToModifierFlags(item.KeyModifiers));
        }

        SendMessage(menu, GetSelector("addItem:"), menuItem);
    }

    private static void AddSubmenu(IntPtr menu, MacMenuItem item)
    {
        var submenu = CreateMenu(item.Title);

        // The parent is a titled item with no action. Clicking it just opens the submenu.
        var parentItem = CreateMenuItem(item.Title, IntPtr.Zero, string.Empty);
        SendMessage(parentItem, GetSelector("setSubmenu:"), submenu);
        SendMessage(menu, GetSelector("addItem:"), parentItem);

        var provider = item.SubmenuItemsProvider;
        if (provider is null)
        {
            return;
        }

        // Rebuild on every open via the delegate, and once now so the parent's initial enabled state (which
        // AppKit derives from whether the submenu holds any enabled item) is correct before first display.
        _dynamicSubmenuProviders[submenu] = provider;
        SendMessage(submenu, GetSelector("setDelegate:"), _commandTarget);
        PopulateDynamicSubmenu(submenu, provider);
    }

    private static void PopulateDynamicSubmenu(IntPtr submenu, Func<IReadOnlyList<MacMenuItem>> provider)
    {
        SendMessage(submenu, GetSelector("removeAllItems"));

        IReadOnlyList<MacMenuItem> items;
        try
        {
            items = provider();
        }
        catch
        {
            // A throw here would unwind through AppKit. Leave the submenu empty rather than crash.
            return;
        }

        foreach (var item in items)
        {
            AddItem(submenu, item);
        }
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
        var allocated = SendMessage(GetClass("NSMenu"), GetSelector("alloc"));
        return SendMessage(allocated, GetSelector("initWithTitle:"), CreateNSString(title));
    }

    private static IntPtr CreateMenuItem(string title, IntPtr action, string keyEquivalent)
    {
        var allocated = SendMessage(GetClass("NSMenuItem"), GetSelector("alloc"));
        return SendMessage(
            allocated,
            GetSelector("initWithTitle:action:keyEquivalent:"),
            CreateNSString(title),
            action,
            CreateNSString(keyEquivalent));
    }

}
