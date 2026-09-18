using System.ComponentModel;

namespace Celbridge.Workspace;

/// <summary>
/// Bindable view of the current project's Workspace-scope settings, for the controls
/// that bind them. Programmatic access uses ISettingsService.
/// </summary>
public interface IBindableWorkspaceSettings : INotifyPropertyChanged
{
    /// <summary>
    /// The workspace areas the user has chosen to see, always including Main, or null while the project has no
    /// saved choice. This is the layout a project opens at and returns to when it leaves Focus or Presentation,
    /// rather than what is on screen while one of those is hiding everything. Setting null clears the choice.
    /// </summary>
    IReadOnlySet<WorkspaceArea>? PreferredVisibleAreas { get; set; }

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
    /// Whether the Explorer draws the resources the project's hide patterns match.
    /// </summary>
    bool ShowHiddenFiles { get; set; }

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
