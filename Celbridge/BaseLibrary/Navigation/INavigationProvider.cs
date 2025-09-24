namespace Celbridge.Navigation;

/// <summary>
/// Abstraction for a UI element that supports application-wide page navigation.
/// </summary>
public interface INavigationProvider
{
    /// <summary>
    /// Navigate the main application UI to the requested page.
    /// </summary>
    Result NavigateToPage(Type pageType);

    /// <summary>
    /// Navigate the main application UI to the requested page.
    /// The target page can access the passed the parameter object during initialization.
    /// </summary>
    Result NavigateToPage(Type pageType, object parameter);

    Result SelectNavigationItemUI(string navItemName);
}
