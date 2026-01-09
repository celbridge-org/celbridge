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
/// Sent when the workspace page has loaded and is about to populate the workspace panels.
/// Workspace services should create their panel views when they receive this message.
/// </summary>
public record WorkspaceWillPopulatePanelsMessage();

/// <summary>
/// Sent when the loaded workspace has finished unloading.
/// </summary>
public record WorkspaceUnloadedMessage();

/// <summary>
/// Sent when the workspace page persistence mode is changed.
/// </summary>
public record SetWorkspacePagePersistenceMessage(bool isPersistant);

/// <summary>
/// Sent when the workspace page is unloaded.
/// </summary>
public record UnloadWorkspacePageMessage();

/// <summary>
/// Sent when the workspace page becomes the active page in the navigation view.
/// </summary>
public record WorkspacePageActivatedMessage();

/// <summary>
/// Sent when the user navigates away from the workspace page to another page.
/// </summary>
public record WorkspacePageDeactivatedMessage();

