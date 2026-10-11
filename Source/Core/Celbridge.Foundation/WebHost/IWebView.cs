namespace Celbridge.WebHost;

/// <summary>
/// A navigation the page is about to make.
/// </summary>
public sealed class WebNavigationStartingEventArgs : EventArgs
{
    public WebNavigationStartingEventArgs(string uri)
    {
        Uri = uri;
    }

    /// <summary>
    /// The address the page is navigating to, or empty if the platform gives none.
    /// </summary>
    public string Uri { get; }

    /// <summary>
    /// Set to true to cancel the navigation.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// How a navigation ended.
/// </summary>
public enum WebNavigationResult
{
    /// <summary>
    /// The page loaded.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The navigation was cancelled before the page arrived, such as by the host.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The navigation ended before its page arrived, because the connection was aborted or the response became a
    /// download.
    /// </summary>
    Aborted,

    /// <summary>
    /// The page could not be loaded.
    /// </summary>
    Failed
}

/// <summary>
/// The outcome of a navigation. ErrorStatus is the platform's name for why the navigation did not succeed. It is
/// empty after a successful navigation.
/// </summary>
public sealed record WebNavigationCompletedEventArgs(WebNavigationResult Result, string ErrorStatus);

/// <summary>
/// The service-facing interface to a web view. Members are called on the UI thread unless their summary says
/// otherwise.
/// </summary>
public interface IWebView
{
    /// <summary>
    /// The resource the view shows. Safe to read from any thread.
    /// </summary>
    ResourceKey Resource { get; }

    /// <summary>
    /// The name assistive technology reports for the view, or the resource's name if none is set. Safe to read
    /// from any thread.
    /// </summary>
    string AccessibleName { get; }

    /// <summary>
    /// Raised when the view is disposed, before it releases anything.
    /// </summary>
    event EventHandler? Closing;

    /// <summary>
    /// Raised before the page navigates. A handler can cancel the navigation.
    /// </summary>
    event EventHandler<WebNavigationStartingEventArgs>? NavigationStarting;

    /// <summary>
    /// Raised when a navigation ends, whether it succeeded or failed.
    /// </summary>
    event EventHandler<WebNavigationCompletedEventArgs>? NavigationCompleted;

    /// <summary>
    /// Raised when the page posts a message to its host over the native message bus. The argument is the message
    /// as JSON.
    /// </summary>
    event EventHandler<string>? WebMessageReceived;

    /// <summary>
    /// Raised when the view may have taken the keyboard, because the view gained focus or a click landed in it. The
    /// event can fire while the view already holds the keyboard.
    /// </summary>
    event EventHandler? FocusGained;

    /// <summary>
    /// Raised when the page reports that the keyboard left it.
    /// </summary>
    event EventHandler? FocusLost;

    /// <summary>
    /// Adds a script that runs before the page's own scripts on every later navigation. Adding the same script
    /// twice has no effect.
    /// </summary>
    Task AddDocumentStartScriptAsync(string script);

    /// <summary>
    /// Evaluates a JavaScript expression and returns its value as JSON. Safe to call from any thread.
    /// </summary>
    Task<string> EvalAsync(string expression);

    /// <summary>
    /// Reloads the page, and clears the HTTP cache first where the platform can. Safe to call from any thread.
    /// </summary>
    Task ReloadAsync(bool clearCache);

    /// <summary>
    /// Captures the page as an image. Fails if the view is not on screen. Safe to call from any thread.
    /// </summary>
    Task<ScreenshotData> CaptureScreenshotAsync(ScreenshotRequest request);
}
