namespace Celbridge.Platform;

/// <summary>
/// The keyboard modifier that issues application commands on the current platform: Control on Windows and
/// Linux, Command on macOS.
/// </summary>
public enum CommandModifierKey
{
    Control,
    Command
}

/// <summary>
/// The single oracle for platform-divergent behaviour. A layer that must behave differently on one platform
/// asks a semantic capability question here rather than checking the operating system or build head directly,
/// so the OS and head branching lives in one place.
///
/// Celbridge runs as a packaged Windows head (WinAppSDK) and a Skia desktop head (Windows, macOS, and Linux).
/// Each capability ends with the platforms it holds on. "The packaged Windows head" means the WinAppSDK build
/// specifically, not the Skia head running on Windows.
/// </summary>
public interface IPlatformInfo
{
    /// <summary>
    /// Whether the platform supplies its own application menu bar, so the in-window hamburger menu is not
    /// mounted. True on macOS.
    /// </summary>
    bool UsesNativeMenuBar { get; }

    /// <summary>
    /// Whether the platform offers full-screen through its own window chrome (the macOS title-bar green
    /// button), so the app does not present its own full-screen toggle. True on macOS.
    /// </summary>
    bool HasNativeFullScreenAffordance { get; }

    /// <summary>
    /// Whether the head draws a custom title bar that the system caption buttons sit over, so the toolbar
    /// reserves a column for them. True on the packaged Windows head only.
    /// </summary>
    bool ReservesWindowCaptionButtons { get; }

    /// <summary>
    /// Whether file and folder pickers must be associated with the main window's handle before use. True on
    /// the packaged Windows head only.
    /// </summary>
    bool PickersRequireWindowHandle { get; }

    /// <summary>
    /// The keyboard modifier that issues application commands. Command on macOS. Control on Windows and Linux.
    /// </summary>
    CommandModifierKey CommandModifier { get; }

    /// <summary>
    /// The localization key for the platform's system file manager name, resolved by the consumer for menu
    /// labels and hints. Names Finder on macOS. Names File Explorer on Windows and Linux.
    /// </summary>
    string FileManagerNameStringKey { get; }

    /// <summary>
    /// Whether the platform treats Backspace as a delete key in addition to Delete, following the macOS
    /// keyboard convention where the main Delete key is Backspace. True on macOS.
    /// </summary>
    bool TreatsBackspaceAsDeleteKey { get; }

    /// <summary>
    /// Whether the platform treats Ctrl+Y as a redo shortcut (in addition to the cross-platform Ctrl+Shift+Z),
    /// following the Windows keyboard convention. True on Windows (both the packaged and Skia heads).
    /// </summary>
    bool TreatsCtrlYAsRedo { get; }

    /// <summary>
    /// Whether list controls should clear their item-container transitions because the platform's add and
    /// remove animations are distracting. True on the packaged Windows head only.
    /// </summary>
    bool SuppressListItemTransitions { get; }

    /// <summary>
    /// Whether a list row's selection visual must be re-asserted after selection changes because the platform
    /// does not repaint the highlight until a later pointer event. True on macOS.
    /// </summary>
    bool RequiresMacOSSelectionRepaint { get; }

    /// <summary>
    /// Whether selecting a tab in an unmeasured overflowing strip can throw a layout exception, so the
    /// selection is retried on the next dispatcher cycle. True on macOS.
    /// </summary>
    bool RequiresMacOSLayoutRetry { get; }

    /// <summary>
    /// Whether the tab strip must be scrolled manually to reveal the selected tab because the platform does
    /// not bring it into view automatically. True on macOS.
    /// </summary>
    bool RequiresMacOSTabScrollIntoView { get; }

    /// <summary>
    /// Whether the mouse wheel must be translated into horizontal tab-strip scrolling manually because the
    /// platform does not scroll the overflowing strip in response to the wheel. True on macOS.
    /// </summary>
    bool RequiresMacOSTabWheelScroll { get; }

    /// <summary>
    /// Whether document tabs are dragged by the pointer-driven drag controller because the platform's
    /// built-in tab drag-and-drop is unreliable. True on the Skia desktop head (all operating systems).
    /// </summary>
    bool UsesPointerDrivenTabDrag { get; }
}
