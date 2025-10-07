namespace Celbridge.UserInterface;

/// <summary>
/// Provides access to core application UI elements.
/// </summary>
public interface IUserInterfaceService
{
    /// <summary>
    /// Returns the main window of the application.
    /// </summary>
    object MainWindow { get; }

    /// <summary>
    /// Returns the XamlRoot of the the application.
    /// This is initialized with the XamlRoot property of the application's RootFrame during startup.
    /// </summary>
    object XamlRoot { get; }

    /// <summary>
    /// Returns the TitleBar of the application.
    /// </summary>
    object TitleBar { get; }

    /// <summary>
    /// Color theme of the user interface
    /// </summary>
    UserInterfaceTheme UserInterfaceTheme { get; set; }

    /// <summary>
    /// Call to register the Titlebar of the application with the UserInterface service.
    /// </summary>
    void RegisterTitleBar(object titleBar);

    /// <summary>
    /// Call to set the current project title when a new project is made or loaded.
    /// </summary>
    void SetCurrentProjectTitle( string currentProjectTitle );
}
