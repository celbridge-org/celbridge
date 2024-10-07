using System.ComponentModel;

namespace Celbridge.Settings;

/// <summary>
/// Manage persistent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    /// <summary>
    /// Gets or sets a value indicating whether the explorer panel is visible.
    /// </summary>
    bool IsExplorerPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the width of the explorer panel.
    /// </summary>
    float ExplorerPanelWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the inspector panel is visible.
    /// </summary>
    bool IsInspectorPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the width of the inspector panel.
    /// </summary>
    float InspectorPanelWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tools panel is visible.
    /// </summary>
    bool IsToolsPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the height of the tools panel.
    /// </summary>
    float ToolsPanelHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the detail panel.
    /// </summary>
    float DetailPanelHeight { get; set; }

    /// <summary>
    /// Gets or sets the previous new project folder path.
    /// </summary>
    string PreviousNewProjectFolderPath { get; set; }

    /// <summary>
    /// Gets or sets the previous project.
    /// </summary>
    string PreviousProject { get; set; }

    /// <summary>
    /// Gets or sets the list of recent projects.
    /// </summary>
    List<string> RecentProjects { get; set; }

    /// <summary>
    /// Gets or sets the OpenAI key.
    /// </summary>
    string OpenAIKey { get; set; }

    /// <summary>
    /// Gets or sets the Sheets API key.
    /// </summary>
    string SheetsAPIKey { get; set; }

    /// <summary>
    /// Resets the settings to their default values.
    /// </summary>
    void Reset();
}
