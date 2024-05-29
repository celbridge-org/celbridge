namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// Sent when the workspace service has been created.
/// The WorkspaceService has the same lifetime as the loaded workspace.
/// </summary>
public record WorkspaceServiceCreatedMessage(IWorkspaceService WorkspaceService);

/// <summary>
/// Sent when the workspace service has been destroyed.
/// </summary>
public record WorkspaceServiceDestroyedMessage();
