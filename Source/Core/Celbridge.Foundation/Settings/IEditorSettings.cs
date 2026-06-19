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
    // Settings Page Options
    // ========================================

    /// <summary>
    /// Application user interface theme.
    /// </summary>
    ApplicationColorTheme Theme { get; set; }

    // ========================================
    // Workshop Connection
    // ========================================

    /// <summary>
    /// The Workshop server URL. Empty when no Workshop is configured.
    /// </summary>
    string WorkshopUrl { get; set; }

    /// <summary>
    /// The Author name recorded as the publisher of packages and pages. Empty
    /// when none is set.
    /// </summary>
    string WorkshopAuthor { get; set; }
}
