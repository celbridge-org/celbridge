using Celbridge.BaseLibrary.Workspace;

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
/// Sent when the workspace has been loaded.
/// The WorkspaceService has the same lifetime as the loaded workspace.
/// </summary>
public record WorkspaceLoadedMessage(IWorkspaceService WorkspaceService);

/// <summary>
/// Sent when the workspace has been unloaded.
/// </summary>
public record WorkspaceUnloadedMessage();
