namespace Celbridge.BaseLibrary.UserInterface;

/// <summary>
/// Abstraction for a UI element that supports application-wide page navigation.
/// </summary>
public interface INavigationProvider
{
    /// <summary>
    /// Navigate the main application UI to the requested page.
    /// </summary>
    Result NavigateToPage(Type pageType);
}
