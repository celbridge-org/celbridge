namespace Celbridge.UserInterface;

/// <summary>
/// Sent when the main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivatedMessage();

/// <summary>
/// Sent when the main window has been deactivated (i.e. lost focus).
/// </summary>
public record MainWindowDeactivatedMessage();

/// <summary>
/// Sent when the active application page changes.
/// </summary>
public record ActivePageChangedMessage(ApplicationPage ActivePage);

/// <summary>
/// Message sent when the window mode changes.
/// </summary>
public record WindowModeChangedMessage(WindowMode WindowMode);

/// <summary>
/// Message sent when the panel visibility changes.
/// </summary>
public record PanelVisibilityChangedMessage(PanelVisibilityFlags PanelVisibility);

/// <summary>
/// Message sent to request the window state (maximized/restored) to be synchronized
/// with the current editor settings.
/// </summary>
public record RestoreWindowStateMessage();

/// <summary>
/// Message sent when the user exits fullscreen mode by dragging the window.
/// This allows the UI to synchronize its state with the actual window state.
/// </summary>
public record ExitedFullscreenViaDragMessage();

/// <summary>
/// Message sent when the focused panel changes.
/// </summary>
public record PanelFocusChangedMessage(FocusablePanel FocusedPanel);

/// <summary>
/// Message sent to request an undo operation.
/// </summary>
public record UndoRequestedMessage();

/// <summary>
/// Message sent to request a redo operation.
/// </summary>
public record RedoRequestedMessage();

/// <summary>
/// Message sent when the Console panel maximized state changes.
/// </summary>
public record ConsoleMaximizedChangedMessage(bool IsMaximized);

/// <summary>
/// Message sent when the layout should be reset to defaults.
/// Listeners should reset their layout state (e.g., document sections).
/// </summary>
public record ResetLayoutRequestedMessage();

