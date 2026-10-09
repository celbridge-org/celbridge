namespace Celbridge.WebHost;

/// <summary>
/// A viewport size the view gave its page. IsArranged is true when the size came from layout.
/// </summary>
public sealed record WebViewViewportSize(double Width, double Height, bool IsArranged);

/// <summary>
/// The web view an owner holds. It adds the members only the owner calls.
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
    /// Raised when the view enters the visual tree.
    /// </summary>
    event EventHandler? Attached;

    /// <summary>
    /// Raised when the view leaves the visual tree.
    /// </summary>
    event EventHandler? Detached;

    /// <summary>
    /// Sets the size to give the page until the view is laid out.
    /// </summary>
    void SetPresentedSize(double width, double height);

    /// <summary>
    /// Raised each time the view sets its page's viewport size.
    /// </summary>
    event EventHandler<WebViewViewportSize>? ViewportSized;

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
    /// Serves a local folder under a virtual host name. Not every platform supports this.
    /// </summary>
    void MapVirtualHost(string hostName, string folderPath);

    /// <summary>
    /// Raised with the new address when a navigation commits. A navigation that never commits, such as one that
    /// becomes a download, raises nothing.
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
    /// Raised when a navigation becomes a download.
    /// </summary>
    event EventHandler? DownloadStarted;
}
