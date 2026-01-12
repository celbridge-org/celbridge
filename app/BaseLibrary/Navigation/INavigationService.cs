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

    /// <summary>
    /// Navigates to a page or performs an action based on a navigation tag.
    /// This is the primary method for handling navigation from UI elements like NavigationView.
    /// </summary>
    Result NavigateByTag(string tag);

    /// <summary>
    /// Clears the persistence settings of all loaded pages. Used before unloading a project to ensure all pages can be flushed.
    /// </summary>
    public void ClearPersistenceOfAllLoadedPages();

    /// <summary>
    /// Performs a forced unload of all the persistant pages which are out of focus. 
    ///  The in focus page will be unloaded normally by the navigation.
    /// </summary>
    public void UnloadPersistantUnfocusedPages();
}
