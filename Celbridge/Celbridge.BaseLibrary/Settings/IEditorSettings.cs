using System.ComponentModel;

namespace Celbridge.BaseLibrary.Settings;

/// <summary>
/// Manage persitent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    bool LeftPanelVisible { get; set; }

    float LeftPanelWidth { get; set; }

    bool RightPanelVisible { get; set; }

    float RightPanelWidth { get; set; }

    bool BottomPanelVisible { get; set; }

    float BottomPanelHeight { get; set; }

    float DetailPanelHeight { get; set; }

    string PreviousNewProjectFolder { get; set; }

    string PreviousActiveProjectPath { get; set; }

    List<string> PreviousOpenDocuments { get; set; }

    string OpenAIKey { get; set; }

    string SheetsAPIKey { get; set; }

    void Reset();
}
