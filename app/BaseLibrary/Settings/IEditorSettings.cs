using Celbridge.UserInterface;
using System.ComponentModel;

namespace Celbridge.Settings;

/// <summary>
/// Manage persistent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    // ========================================
    // Settings Management
    // ========================================

    /// <summary>
    /// Resets the settings to their default values.
    /// </summary>
    void Reset();

    // ========================================
    // Previous Paths and History
    // ========================================

    /// <summary>
    /// The previously specified new project folder path.
    /// </summary>
    string PreviousNewProjectFolderPath { get; set; }

    /// <summary>
    /// The previously loaded Celbridge project file.
    /// </summary>
    string PreviousProject { get; set; }

    /// <summary>
    /// The list of recently loaded project files.
    /// </summary>
    List<string> RecentProjects { get; set; }

    /// <summary>
    /// The file extension of the previously created file via the Add File dialog.
    /// </summary>
    string PreviousNewFileExtension { get; set; }

    // ========================================
    // Window Geometry
    // Applies to non-maximized windowed mode only.
    // ========================================

    /// <summary>
    /// Is the the window maximized.
    /// </summary>
    bool IsWindowMaximized { get; set; }

    /// <summary>
    /// Preferred window X position when in non-maximized windowed mode.
    /// </summary>
    int PreferredWindowX { get; set; }

    /// <summary>
    /// Preferred window Y position when in non-maximized windowed mode.
    /// </summary>
    int PreferredWindowY { get; set; }

    /// <summary>
    /// Preferred window width when in non-maximized windowed mode.
    /// </summary>
    int PreferredWindowWidth { get; set; }

    /// <summary>
    /// Preferred window height when in non-maximized windowed mode.
    /// </summary>
    int PreferredWindowHeight { get; set; }

    // ========================================
    // Panel State
    // ========================================

    /// <summary>
    /// Preferred panel visibility.
    /// </summary>
    PanelVisibilityFlags PreferredPanelVisibility { get; set; }

    /// <summary>
    /// Width of the Context panel.
    /// </summary>
    float ContextPanelWidth { get; set; }

    /// <summary>
    /// Width of the Inspector panel.
    /// </summary>
    float InspectorPanelWidth { get; set; }

    /// <summary>
    /// Height of the Console panel.
    /// </summary>
    float ConsolePanelHeight { get; set; }

    /// <summary>
    /// Height of the detail panel.
    /// </summary>
    float DetailPanelHeight { get; set; }

    // ========================================
    // Settings Page Options
    // ========================================

    /// <summary>
    /// Application user interface theme.
    /// </summary>
    ApplicationColorTheme Theme { get; set; }
}
