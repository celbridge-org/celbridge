namespace Celbridge.Navigation;

/// <summary>
/// Abstraction for a UI element that supports application-wide page navigation.
/// </summary>
public interface INavigationProvider
{
    public const string HomeTag = "Home";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string SettingsTag = "Settings";
    public const string SearchTag = "Search";
    public const string ExplorerTag = "Explorer";
    public const string CommunityTag = "Community";
    public const string DebugTag = "Debug";
    public const string RevisionControlTag = "RevisionControl";

    /// <summary>
    /// Navigate the main application UI to the requested page.
    /// </summary>
    Result NavigateToPage(Type pageType);

    /// <summary>
    /// Navigate the main application UI to the requested page.
    /// The target page can access the passed the parameter object during initialization.
    /// </summary>
    Result NavigateToPage(Type pageType, object parameter);

    /// <summary>
    /// Select the specified Navigation Item in our Main Navigaion View by name.
    /// </summary>
    Result SelectNavigationItemByNavigationTag(string navigationTag);

    /// <summary>
    /// Return the Instance Name of the page currently displayed in the NavigationView Panel, or "None".
    /// </summary>
    string GetCurrentPageName();
}
