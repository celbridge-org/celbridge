using System.ComponentModel;
using Celbridge.UserInterface;

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

    /// <summary>
    /// The template name of the previously created project via the New Project dialog.
    /// </summary>
    string PreviousNewProjectTemplateName { get; set; }

    // ========================================
    // Window Geometry
    // Applies to non-maximized windowed mode only.
    // ========================================

    /// <summary>
    /// Indicates whether saved window geometry should be used on startup.
    /// When false, the OS default window position and size will be used.
    /// </summary>
    bool UsePreferredWindowGeometry { get; set; }

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
    PanelRegion PreferredPanelVisibility { get; set; }

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
    /// Height of the detail panel.
    /// </summary>
    float DetailPanelHeight { get; set; }

    /// <summary>
    /// Whether the Console panel is maximized to fill the Documents area.
    /// </summary>
    bool IsConsoleMaximized { get; set; }

    /// <summary>
    /// The console panel height before it was maximized (used for restore).
    /// </summary>
    float RestoreConsoleHeight { get; set; }

    // ========================================
    // Settings Page Options
    // ========================================

    /// <summary>
    /// Application user interface theme.
    /// </summary>
    ApplicationColorTheme Theme { get; set; }

    // ========================================
    // Search Panel Options
    // ========================================

    /// <summary>
    /// Match case option for search panel.
    /// </summary>
    bool SearchMatchCase { get; set; }

    /// <summary>
    /// Match whole word option for search panel.
    /// </summary>
    bool SearchWholeWord { get; set; }
}
