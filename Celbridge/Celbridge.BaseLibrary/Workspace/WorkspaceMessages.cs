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

/// <summary>
/// Sent when the workspace has been initialized and is ready to be used.
/// </summary>
public record WorkspaceInitializedMessage();
