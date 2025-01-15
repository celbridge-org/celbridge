using Celbridge.Forms;

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
    /// Color theme of the user interface
    /// </summary>
    UserInterfaceTheme UserInterfaceTheme { get; }
}
