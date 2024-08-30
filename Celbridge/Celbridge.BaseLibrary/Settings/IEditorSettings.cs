using System.ComponentModel;

namespace Celbridge.Settings;

/// <summary>
/// Manage persitent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    bool IsLeftPanelVisible { get; set; }

    float LeftPanelWidth { get; set; }

    bool IsRightPanelVisible { get; set; }

    float RightPanelWidth { get; set; }

    bool IsBottomPanelVisible { get; set; }

    float BottomPanelHeight { get; set; }

    float DetailPanelHeight { get; set; }

    string PreviousNewProjectFolderPath { get; set; }

    string PreviousLoadedProject { get; set; }

    List<string> PreviousOpenDocuments { get; set; }

    string PreviousSelectedDocument { get; set; }

    string OpenAIKey { get; set; }

    string SheetsAPIKey { get; set; }

    void Reset();
}
