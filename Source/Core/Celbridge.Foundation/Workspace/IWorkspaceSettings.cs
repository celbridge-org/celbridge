using System.ComponentModel;

namespace Celbridge.Workspace;

/// <summary>
/// Typed, bindable facade over the Workspace-scope setting descriptors for the
/// current loaded project. Each property reads and writes its descriptor through
/// the settings service and raises PropertyChanged from its setter, so views can
/// bind to named panel, search, and editor state that persists per project.
/// Distinct from IWorkspacePropertyBag, the dynamic key/value bag.
/// </summary>
public interface IWorkspaceSettings : INotifyPropertyChanged
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
