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
/// Sent when the workspace state needs to be saved.
/// </summary>
public record WorkspaceStateDirtyMessage();

/// <summary>
/// Message sent when the surface visibility changes.
/// </summary>
public record SurfaceVisibilityChangedMessage(WorkspaceSurface SurfaceVisibility);

/// <summary>
/// Sent to request a brief attention flash around the perimeter of a surface the user has just revealed.
/// </summary>
public record FlashSurfaceMessage(WorkspaceSurface Surface);

/// <summary>
/// Message sent when the Bottom document area's alignment changes.
/// </summary>
public record BottomAreaAlignmentChangedMessage(BottomAreaAlignment Alignment);

/// <summary>
/// Message sent when the focused panel changes.
/// </summary>
public record PanelFocusChangedMessage(WorkspacePanelId FocusedPanel);

/// <summary>
/// Sent when the surface shown in the Utility Panel rail changes. UtilityId is the fully-qualified id of the
/// now-active utility (a built-in id such as "celbridge.explorer", or a custom id), or empty when none.
/// </summary>
public record ActiveUtilityChangedMessage(string UtilityId);

/// <summary>
/// Message sent when the layout should be reset to defaults.
/// </summary>
public record ResetLayoutRequestedMessage();
