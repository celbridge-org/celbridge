namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// The types of panel that can be displayed in the workspace.
/// </summary>
public enum WorkspacePanelType
{
    ProjectPanel,
    ConsolePanel,
    InspectorPanel,
    StatusPanel,
    DocumentsPanel
}

/// <summary>
/// Maps a panel type to a view type.
/// The view type for each panel is instantiated when the workspace is loaded.
/// </summary>
public record WorkspacePanelConfig(WorkspacePanelType PanelType, Type ViewType);
