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
/// Message sent when the window's fullscreen state changes.
/// </summary>
public record FullScreenChangedMessage(bool IsFullScreen);

/// <summary>
/// Message sent to request the window state (maximized/restored) to be synchronized
/// with the current editor settings.
/// </summary>
public record RestoreWindowStateMessage();

/// <summary>
/// Message sent when the user exits fullscreen mode by dragging the window.
/// </summary>
public record ExitedFullscreenViaDragMessage();

/// <summary>
/// Message sent to request an undo operation.
/// </summary>
public record UndoRequestedMessage();

/// <summary>
/// Message sent to request a redo operation.
/// </summary>
public record RedoRequestedMessage();

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

