namespace Celbridge.Navigation;

/// <summary>
/// A service that supports top-level page navigation in the application.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Returns the page navigation provider.
    /// </summary>
    INavigationProvider NavigationProvider { get; }

    /// <summary>
    /// Registers a page with the navigation system using a navigation tag.
    /// The page type must inherit from Windows.UI.Xaml.Controls.Page class.
    /// </summary>
    Result RegisterPage(string tag, Type pageType);

    /// <summary>
    /// Unregisters a previously registered page.
    /// </summary>
    Result UnregisterPage(string tag);

    /// <summary>
    /// Navigates the top-level UI to display the specified page by tag.
    /// </summary>
    Result NavigateToPage(string tag);

    /// <summary>
    /// Navigates the top-level UI to display the specified page by tag, passing an object parameter.
    /// </summary>
    Result NavigateToPage(string tag, object parameter);

    /// <summary>
    /// When true, the WorkspacePage should perform cleanup when it unloads.
    /// This flag is automatically cleared after navigation completes.
    /// </summary>
    bool IsWorkspacePageCleanupPending { get; }

    /// <summary>
    /// Signals that the WorkspacePage should perform cleanup on next unload.
    /// This should be called before unloading or switching projects to ensure the
    /// WorkspacePage is fully recreated on the next navigation.
    /// </summary>
    void RequestWorkspacePageCleanup();
}
