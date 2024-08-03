namespace Celbridge.Workspace;

/// <summary>
/// Sent when the workspace service has been created.
/// The WorkspaceService has the same lifetime as the loaded workspace.
/// </summary>
public record WorkspaceServiceCreatedMessage(IWorkspaceService WorkspaceService);

/// <summary>
/// Sent when the workspace has finished loading and is ready to be used.
/// </summary>
public record WorkspaceLoadedMessage();

/// <summary>
/// Sent when the loaded workspace has finished unloading.
/// </summary>
public record WorkspaceUnloadedMessage();

/// <summary>
/// A message to request an update of the resource registry.
/// </summary>
public record RequestSaveWorkspaceStateMessage;
