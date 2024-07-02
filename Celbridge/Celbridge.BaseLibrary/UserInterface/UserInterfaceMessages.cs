namespace Celbridge.BaseLibrary.UserInterface;

/// <summary>
/// Sent when the main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivatedMessage();

/// <summary>
/// Sent when the main window has been deactivated (i.e. lost focus).
/// </summary>
public record MainWindowDeactivatedMessage();

/// <summary>
/// Sent when the user performs a keyboard shortcut action.
/// </summary>
public record ShortcutActionMessage(ShortcutAction shortcutAction);