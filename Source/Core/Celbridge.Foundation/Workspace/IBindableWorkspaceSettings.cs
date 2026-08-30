using System.ComponentModel;

namespace Celbridge.Workspace;

/// <summary>
/// Bindable view of the current project's Workspace-scope settings, for the controls
/// that bind them. Programmatic access uses ISettingsService.
/// </summary>
public interface IBindableWorkspaceSettings : INotifyPropertyChanged
{
    /// <summary>
    /// The workspace areas the user prefers to see, always including Main. This is the layout a project
    /// opens at and returns to when it leaves Focus or Presentation, rather than what is on screen while one
    /// of those is hiding everything. Reports every area for a project whose layout has never been customised.
    /// </summary>
    IReadOnlySet<WorkspaceArea> PreferredVisibleAreas { get; set; }

    /// <summary>
    /// Width of the Utility Panel.
    /// </summary>
    float UtilityPanelWidth { get; set; }

    /// <summary>
    /// Width of the Side document area.
    /// </summary>
    float SideAreaWidth { get; set; }

    /// <summary>
    /// Height of the Bottom document area.
    /// </summary>
    float BottomAreaHeight { get; set; }

    /// <summary>
    /// How far the Bottom document area spans across the workspace.
    /// </summary>
    BottomAreaAlignment BottomAreaAlignment { get; set; }

    /// <summary>
    /// Match case option for the search panel.
    /// </summary>
    bool SearchMatchCase { get; set; }

    /// <summary>
    /// Match whole word option for the search panel.
    /// </summary>
    bool SearchWholeWord { get; set; }

    /// <summary>
    /// Whether replace mode is enabled in the search panel.
    /// </summary>
    bool ReplaceMode { get; set; }

    /// <summary>
    /// The file extension of the previously created file via the Add File dialog.
    /// </summary>
    string PreviousNewFileExtension { get; set; }
}
