namespace Celbridge.Workspace;

/// <summary>
/// Maps a panel type to a view type.
/// The view type for each panel is instantiated when the workspace is loaded.
/// </summary>
public record WorkspacePanelConfig(WorkspacePanel WorkspacePanel, Type ViewType);
