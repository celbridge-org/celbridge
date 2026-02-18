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
/// Sent when the workspace page becomes the active page in the navigation view.
/// </summary>
public record WorkspacePageActivatedMessage();

/// <summary>
/// Sent when the user navigates away from the workspace page to another page.
/// </summary>
public record WorkspacePageDeactivatedMessage();

/// <summary>
/// Sent when the workspace state needs to be saved.
/// </summary>
public record WorkspaceStateDirtyMessage();

/// <summary>
/// Message sent when the region visibility changes.
/// </summary>
public record RegionVisibilityChangedMessage(LayoutRegion RegionVisibility);

/// <summary>
/// Message sent when the focused panel changes.
/// </summary>
public record PanelFocusChangedMessage(WorkspacePanel FocusedPanel);

/// <summary>
/// Message sent when the Console panel maximized state changes.
/// </summary>
public record ConsoleMaximizedChangedMessage(bool IsMaximized);

/// <summary>
/// Message sent when the layout should be reset to defaults.
/// Listeners should reset their layout state (e.g., document sections).
/// </summary>
public record ResetLayoutRequestedMessage();
