namespace Celbridge.Documents;

/// <summary>
/// Pool for managing WebView2 instances used by the text editor.
/// This provides efficient reuse of WebView2 controls which are expensive to create.
/// </summary>
public interface ITextEditorWebViewPool
{
    /// <summary>
    /// Acquires a WebView2 instance from the pool.
    /// If the pool is empty, a new instance will be created.
    /// </summary>
    Task<WebView2> AcquireInstance();

    /// <summary>
    /// Releases a WebView2 instance back to the pool.
    /// The instance will be closed and a fresh one created for the pool.
    /// </summary>
    Task ReleaseInstanceAsync(WebView2 webView);

    /// <summary>
    /// Shuts down the pool and disposes all WebView2 instances.
    /// </summary>
    void Shutdown();
}
