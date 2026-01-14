namespace Celbridge.Workspace;

/// <summary>
/// Defines the available views within the ProjectPanel.
/// </summary>
public enum ProjectPanelView
{
    /// <summary>
    /// No view selected.
    /// </summary>
    None,

    /// <summary>
    /// The Explorer view for browsing project files.
    /// </summary>
    Explorer,

    /// <summary>
    /// The Search view for searching within the project.
    /// </summary>
    Search,

    /// <summary>
    /// The Debug view for debugging functionality.
    /// </summary>
    Debug,

    /// <summary>
    /// The Version Control view for source control operations.
    /// </summary>
    VersionControl
}
