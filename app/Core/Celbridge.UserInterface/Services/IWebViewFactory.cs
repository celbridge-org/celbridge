namespace Celbridge.UserInterface;

/// <summary>
/// Pre-warming factory for WebView2 instances.
/// Clients are responsible for closing/disposing instances when done.
/// </summary>
public interface IWebViewFactory
{
    /// <summary>
    /// Acquires a WebView2 instance from the pool.
    /// If the pool is empty, a new instance will be created.
    /// The returned WebView2 has CoreWebView2 initialized but has not navigated to any URL.
    /// The caller is responsible for closing the WebView2 when done.
    /// </summary>
    Task<WebView2> AcquireAsync();

    /// <summary>
    /// Shuts down the pool and disposes all WebView2 instances.
    /// </summary>
    void Shutdown();
}
