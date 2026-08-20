using Celbridge.Settings;

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
    /// Returns the XamlRoot of the application.
    /// </summary>
    object XamlRoot { get; }

    /// <summary>
    /// Color theme of the user interface
    /// </summary>
    UserInterfaceTheme UserInterfaceTheme { get; set; }

    /// <summary>
    /// The currently active application page.
    /// </summary>
    ApplicationPage ActivePage { get; }

    /// <summary>
    /// Sets the active application page and broadcasts an ActivePageChangedMessage.
    /// </summary>
    void SetActivePage(ApplicationPage page);

    /// <summary>
    /// Applies the currently selected theme to the UserInterface.
    /// </summary>
    void ApplyCurrentTheme();

    /// <summary>
    /// Selects the application colour theme, persisting it and applying it immediately.
    /// </summary>
    void SetTheme(ApplicationColorTheme theme);
}
