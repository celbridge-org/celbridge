namespace Celbridge.UserInterface;

/// <summary>
/// Sent when the main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivatedMessage();

/// <summary>
/// Sent when the main window has been deactivated (i.e. another application took the keyboard).
/// </summary>
public record MainWindowDeactivatedMessage();

/// <summary>
/// Sent when a modal dialog takes the keyboard, and again when it gives it back. A hosted web surface sees
/// the dialog opening as an ordinary blur and cannot tell it from the user clicking away, so the surface
/// that had the keyboard keeps it for as long as the dialog holds it.
/// </summary>
public record ModalDialogOpenedMessage();

/// <summary>
/// Sent when a modal dialog closes and the keyboard can return to the surface that had it.
/// </summary>
public record ModalDialogClosedMessage();

/// <summary>
/// Sent when the main page has finished loading at startup.
/// </summary>
public record MainPageLoadedMessage();

/// <summary>
/// Message sent when the layout mode (chrome level) changes.
/// </summary>
public record LayoutModeChangedMessage(LayoutMode LayoutMode);

/// <summary>
/// Message sent when the app asks for the window to enter or leave full screen. The layout manager sends the message.
/// The window state helper moves the window to match, and the layout toolbar updates its full screen toggle.
/// </summary>
public record FullScreenRequestedMessage(bool IsFullScreen);

/// <summary>
/// Message sent when the window's presenter changes between maximized and not maximized.
/// </summary>
public record WindowMaximizedChangedMessage(bool IsMaximized);

/// <summary>
/// Message sent to request the window state (maximized/restored) to be synchronized
/// with the current editor settings.
/// </summary>
public record RestoreWindowStateMessage();

/// <summary>
/// Message sent when the window enters or leaves full screen, including changes the app did not ask for. On Windows
/// the user can drag the window down from the top of the screen. On macOS the user can press the window's full
/// screen button. The window state helper and the macOS fullscreen controller send the message, and the layout
/// manager follows the window's state.
/// </summary>
public record WindowFullScreenChangedMessage(bool IsFullScreen);

/// <summary>
/// Message sent to request closing the active document tab.
/// </summary>
public record CloseActiveDocumentRequestedMessage();

/// <summary>
/// Message sent to request closing all document tabs in the active document's section.
/// </summary>
public record CloseAllDocumentsRequestedMessage();

/// <summary>
/// Sent to request a brief attention flash on an open document's tab. A transient view effect with no state
/// change, so it is a notification rather than a command. A no-op when the document is not open.
/// </summary>
public record FlashDocumentMessage(ResourceKey FileResource);

/// <summary>
/// Message sent when the application theme changes.
/// </summary>
public record ThemeChangedMessage(UserInterfaceTheme Theme);

