namespace Celbridge.WebHost;

/// <summary>
/// Options for a whole-page find. Find always wraps, as a browser's does. OnMatchStateChanged is called on the UI
/// thread as the find advances.
/// </summary>
public sealed record FindOptions(
    bool CaseSensitive = false,
    Action<FindMatchState>? OnMatchStateChanged = null);

/// <summary>
/// The progress of a find. MatchFound says whether there is a match to step to. MatchCount and ActiveMatchIndex,
/// counted from 1, are null when the platform does not report them.
/// </summary>
public sealed record FindMatchState(bool MatchFound, int? MatchCount = null, int? ActiveMatchIndex = null);

/// <summary>
/// The owner-facing interface to a web view. It adds the members that only the view's owner calls.
/// </summary>
public interface IEditorWebView : IWebView, IDisposable
{
    /// <summary>
    /// Sets the resource the view shows.
    /// </summary>
    void SetResource(ResourceKey resource);

    /// <summary>
    /// Sets the name assistive technology reports for the view. An empty name falls back to the resource's name.
    /// </summary>
    void SetAccessibleName(string accessibleName);

    /// <summary>
    /// Adds the view to a container, or moves it to a new one. The page keeps running.
    /// </summary>
    void AttachTo(Panel container);

    /// <summary>
    /// Sets the size to give the page until the view is laid out.
    /// </summary>
    void SetPresentedSize(double width, double height);

    /// <summary>
    /// True when the view can give its page a viewport size before the view is laid out.
    /// </summary>
    bool CanSizeUnarrangedViewport { get; }

    /// <summary>
    /// True when the page's viewport has a real size, and false while it has only a placeholder.
    /// </summary>
    bool IsSized { get; }

    /// <summary>
    /// Raised when IsSized changes.
    /// </summary>
    event EventHandler? IsSizedChanged;

    /// <summary>
    /// The address of the current page.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Navigates to an absolute URL.
    /// </summary>
    void Navigate(string url);

    /// <summary>
    /// Loads an HTML string as a page whose origin is baseUrl.
    /// </summary>
    void LoadHtmlString(string html, string baseUrl);

    /// <summary>
    /// True when a page loaded from a mapped virtual host has that virtual host as its origin.
    /// </summary>
    bool SupportsVirtualHostMapping { get; }

    /// <summary>
    /// Serves a local folder under a virtual host name. A page loaded from the virtual host has that host as its
    /// origin only where SupportsVirtualHostMapping is true.
    /// </summary>
    void MapVirtualHost(string hostName, string folderPath);

    /// <summary>
    /// Raised with the new address when a navigation commits, and only then. A navigation that becomes a download
    /// never commits.
    /// </summary>
    event EventHandler<string>? NavigationCommitted;

    /// <summary>
    /// Raised with the address of a window the page asks to open. The window never opens.
    /// </summary>
    event EventHandler<string>? NewWindowRequested;

    /// <summary>
    /// True when the page's history has an entry to go back to.
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// True when the page's history has an entry to go forward to.
    /// </summary>
    bool CanGoForward { get; }

    /// <summary>
    /// Goes back one entry in the page's history.
    /// </summary>
    void GoBack();

    /// <summary>
    /// Goes forward one entry in the page's history.
    /// </summary>
    void GoForward();

    /// <summary>
    /// Raised when the page's history changes.
    /// </summary>
    event EventHandler? HistoryChanged;

    /// <summary>
    /// Stops the navigation in progress.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// True when the page has a find bar of its own, so the owner adds none.
    /// </summary>
    bool ProvidesBuiltInFind { get; }

    /// <summary>
    /// Finds a term in the page and selects the first match. A new call replaces the find in progress. Not every
    /// platform supports this.
    /// </summary>
    Task StartFindAsync(string term, FindOptions options);

    /// <summary>
    /// Selects the next match of the find in progress.
    /// </summary>
    void FindNext();

    /// <summary>
    /// Selects the previous match of the find in progress.
    /// </summary>
    void FindPrevious();

    /// <summary>
    /// Ends the find in progress and clears its selection. Safe to call when no find is in progress.
    /// </summary>
    void StopFind();

    /// <summary>
    /// Returns the page's health as observed so far.
    /// </summary>
    WebViewHealth GetHealth();

    /// <summary>
    /// Raised when a navigation becomes a download. The navigation then completes as aborted.
    /// </summary>
    event EventHandler? DownloadStarted;

    /// <summary>
    /// Raised when the page turns out to be an empty document. An empty document is a load that failed but reported
    /// success. The view checks the page after each successful navigation and each time the view is attached. A
    /// check is dropped if a navigation leaves the page before the check finishes.
    /// </summary>
    event EventHandler? LoadedEmpty;
}
