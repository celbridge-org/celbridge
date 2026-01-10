using System.ComponentModel;

namespace Celbridge.Settings;

/// <summary>
/// Manage persistent user settings via named setting containers.
/// </summary>
public interface IEditorSettings : INotifyPropertyChanged
{
    // ========================================
    // Panel Visibility and Dimensions
    // ========================================

    /// <summary>
    /// Gets or sets a value indicating whether the Context panel is visible.
    /// </summary>
    bool IsContextPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the width of the Context panel.
    /// </summary>
    float ContextPanelWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the inspector panel is visible.
    /// </summary>
    bool IsInspectorPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the width of the inspector panel.
    /// </summary>
    float InspectorPanelWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the console panel is visible.
    /// </summary>
    bool IsConsolePanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the height of the console panel.
    /// </summary>
    float ConsolePanelHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the detail panel.
    /// </summary>
    float DetailPanelHeight { get; set; }

    // ========================================
    // Previous Paths and History
    // ========================================

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

    // ========================================
    // API Keys
    // ========================================

    /// <summary>
    /// Gets or sets the OpenAI key.
    /// </summary>
    string OpenAIKey { get; set; }

    /// <summary>
    /// Gets or sets the Sheets API key.
    /// </summary>
    string SheetsAPIKey { get; set; }

    // ========================================
    // Layout Management
    // ========================================

    /// <summary>
    /// Resets the settings to their default values.
    /// </summary>
    void Reset();

    /// <summary>
    /// Resets the panel layout to default visibility and sizes.
    /// </summary>
    void ResetPanelLayout();

    // ========================================
    // Application Theme
    // ========================================

    /// <summary>
    /// Gets or Sets the Application User Interface Theme value.
    /// </summary>
    ApplicationColorTheme Theme { get; set; }

    // ========================================
    // Window State and Position
    // ========================================

    /// <summary>
    /// Gets or sets whether the window is maximized.
    /// </summary>
    bool IsWindowMaximized { get; set; }

    /// <summary>
    /// Gets or sets the window X position.
    /// </summary>
    int WindowX { get; set; }

    /// <summary>
    /// Gets or sets the window Y position.
    /// </summary>
    int WindowY { get; set; }

    /// <summary>
    /// Gets or sets the window width.
    /// </summary>
    int WindowWidth { get; set; }

    /// <summary>
    /// Gets or sets the window height.
    /// </summary>
    int WindowHeight { get; set; }

    /// <summary>
    /// Gets or sets the file extension of the last file created via the Add File dialog.
    /// </summary>
    string PreviousNewFileExtension { get; set; }

    // ========================================
    // Layout Mode State
    // ========================================
    // Layout modes control the window state and panel visibility.
    // - Windowed: Not fullscreen, all panels and titlebar visible
    // - FullScreen: Fullscreen with all panels and titlebar visible
    // - ZenMode: Fullscreen, only documents panel (including tab bar) visible
    // - Presenter: Fullscreen with only document content visible (no tab bar)

    /// <summary>
    /// Gets or sets the current layout mode.
    /// Note: This is persisted but the application always starts in Windowed mode.
    /// </summary>
    LayoutMode LayoutMode { get; set; }

    /// <summary>
    /// Gets or sets the Context panel visibility before entering a fullscreen mode.
    /// Used to restore the panel state when returning to Windowed mode.
    /// </summary>
    bool FullscreenPreContextPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the Inspector panel visibility before entering a fullscreen mode.
    /// Used to restore the panel state when returning to Windowed mode.
    /// </summary>
    bool FullscreenPreInspectorPanelVisible { get; set; }

    /// <summary>
    /// Gets or sets the Console panel visibility before entering a fullscreen mode.
    /// Used to restore the panel state when returning to Windowed mode.
    /// </summary>
    bool FullscreenPreConsolePanelVisible { get; set; }
}
