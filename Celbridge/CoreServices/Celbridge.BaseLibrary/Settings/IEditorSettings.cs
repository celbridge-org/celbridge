using System.ComponentModel;

namespace Celbridge.Settings;

/// <summary>
/// Manage persitent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    bool IsExplorerPanelVisible { get; set; }

    float ExplorerPanelWidth { get; set; }

    bool IsInspectorPanelVisible { get; set; }

    float InspectorPanelWidth { get; set; }

    bool IsToolsPanelVisible { get; set; }

    float ToolsPanelHeight { get; set; }

    float DetailPanelHeight { get; set; }

    string PreviousNewProjectFolderPath { get; set; }

    string PreviousLoadedProject { get; set; }

    List<string> RecentProjects { get; set; }

    string OpenAIKey { get; set; }

    string SheetsAPIKey { get; set; }

    void Reset();
}
