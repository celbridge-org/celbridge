namespace Celbridge.WebHost;

/// <summary>
/// Hands out web views, some of them created ahead of time.
/// </summary>
public interface IWebViewFactory
{
    /// <summary>
    /// Returns a web view set up with the given options. The page is ready but has not navigated. The caller
    /// owns the view and disposes it.
    /// </summary>
    Task<IEditorWebView> AcquireAsync(WebViewOptions options);

    /// <summary>
    /// Returns a web view for answering a page's new window request. The caller places the view in a container, and
    /// passes the view to the request's AcceptAsync. The caller then owns the view and disposes of the view.
    /// </summary>
    Task<IEditorWebView> AcquireForNewWindowAsync(WebNewWindowRequest request, WebViewOptions options);

    /// <summary>
    /// Closes the views created ahead of time. Views already handed out are left to their owners.
    /// </summary>
    void Shutdown();
}
