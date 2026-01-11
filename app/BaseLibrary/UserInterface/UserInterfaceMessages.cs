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
/// Message sent when the window layout changes.
/// </summary>
public record WindowLayoutChangedMessage(WindowLayout WindowLayout);
