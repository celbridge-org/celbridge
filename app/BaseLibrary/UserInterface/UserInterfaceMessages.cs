using Celbridge.Settings;

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
/// Message sent when the primary/secondary panel swap setting changes.
/// </summary>
public record PanelSwapChangedMessage(bool IsSwapped);
