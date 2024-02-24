namespace Celbridge.CommonServices.UserInterface;

/// <summary>
/// A service that supports application-wide UI operations.
/// </summary>
public interface IUserInterfaceService
{
    Window MainWindow { get; }

    /// <summary>
    /// Registers a page with the navigation system.
    /// The page type must inherit from Windows.UI.Xaml.Controls.Page class
    /// https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.page?view=winrt-22621
    /// </summary>
    Result RegisterPage(string pageName, Type pageType);

    /// <summary>
    /// Unregisters a previously registered page.
    /// </summary>
    Result UnregisterPage(string pageName);

    /// <summary>
    /// Navigates the top-level UI to display the specified page.
    /// </summary>
    Result NavigateToPage(string pageName);
}
