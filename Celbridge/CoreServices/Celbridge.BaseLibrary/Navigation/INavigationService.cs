using Celbridge.Foundation;

namespace Celbridge.Navigation;

/// <summary>
/// A service that supports page UI navigation.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Returns the page navigation provider.
    /// </summary>
    INavigationProvider NavigationProvider { get; }

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

    /// <summary>
    /// Navigates the top-level UI to display the specified page, passing an object parameter.
    /// </summary>
    Result NavigateToPage(string pageName, object parameter);
}
