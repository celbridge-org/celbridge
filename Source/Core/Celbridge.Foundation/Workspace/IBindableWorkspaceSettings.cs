using System.ComponentModel;

namespace Celbridge.Workspace;

/// <summary>
/// Bindable view of the current project's Workspace-scope settings, for the controls
/// that bind them. Programmatic access uses ISettingsService.
/// </summary>
public interface IBindableWorkspaceSettings : INotifyPropertyChanged
{
    /// <summary>
    /// Preferred visibility of the workspace panel regions.
    /// </summary>
    LayoutRegion PreferredRegionVisibility { get; set; }

    /// <summary>
    /// Width of the Primary panel.
    /// </summary>
    float PrimaryPanelWidth { get; set; }

    /// <summary>
    /// Width of the Secondary panel.
    /// </summary>
    float SecondaryPanelWidth { get; set; }

    /// <summary>
    /// Height of the Console panel.
    /// </summary>
    float ConsolePanelHeight { get; set; }

    /// <summary>
    /// Height of the Detail panel.
    /// </summary>
    float DetailPanelHeight { get; set; }

    /// <summary>
    /// Whether the Console panel is maximized to fill the Documents area.
    /// </summary>
    bool IsConsoleMaximized { get; set; }

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
