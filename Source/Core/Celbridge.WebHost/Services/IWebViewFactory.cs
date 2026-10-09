namespace Celbridge.WebHost;

/// <summary>
/// Hands out web views. It creates some ahead of time, so a document can open without waiting for one.
/// </summary>
public interface IWebViewFactory
{
    /// <summary>
    /// Returns a web view set up with the given options. The page is ready but has not navigated. The caller
    /// owns the view and disposes it.
    /// </summary>
    Task<IEditorWebView> AcquireAsync(WebViewOptions options);

    /// <summary>
    /// Closes the views created ahead of time. Views already handed out are left to their owners.
    /// </summary>
    void Shutdown();
}
