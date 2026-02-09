using Celbridge.Explorer;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Defines the available tabs within the ActivityPanel.
/// </summary>
public enum ActivityPanelTab
{
    /// <summary>
    /// No tab selected.
    /// </summary>
    None,

    /// <summary>
    /// The Explorer tab for browsing project files.
    /// </summary>
    Explorer,

    /// <summary>
    /// The Search tab for searching within the project.
    /// </summary>
    Search,

    /// <summary>
    /// The Debug tab for debugging functionality.
    /// </summary>
    Debug,

    /// <summary>
    /// The Source Control tab for source control operations.
    /// </summary>
    SourceControl
}

/// <summary>
/// Interface for the Activity Panel, which contains the Explorer and Search sub-panels.
/// </summary>
public interface IActivityPanel
{
    /// <summary>
    /// Gets the Explorer Panel for browsing project resources.
    /// </summary>
    IExplorerPanel ExplorerPanel { get; }

    /// <summary>
    /// Gets the Search Panel for searching within the project.
    /// </summary>
    ISearchPanel SearchPanel { get; }

    /// <summary>
    /// Gets the currently active panel tab.
    /// </summary>
    ActivityPanelTab CurrentTab { get; }

    /// <summary>
    /// Shows the specified panel tab and hides all others.
    /// </summary>
    void ShowTab(ActivityPanelTab tab);
}

