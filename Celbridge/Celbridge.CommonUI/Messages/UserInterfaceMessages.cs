using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.Messages;

/// <summary>
/// The WorkspaceView page has been loaded.
/// </summary>
public record WorkspaceViewLoadedMessage(WorkspaceView workspace);

/// <summary>
/// The WorkspaceView page has been unloaded.
/// </summary>
public record WorkspaceViewUnloadedMessage();

/// <summary>
/// The main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivated();

/// <summary>
/// The main window has been deactivated (i.e. lost focus).
/// </summary>
public record MainWindowDeactivated();



