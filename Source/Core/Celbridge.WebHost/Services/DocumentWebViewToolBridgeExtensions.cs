namespace Celbridge.WebHost;

/// <summary>
/// Registers web views with the tool bridge.
/// </summary>
public static class DocumentWebViewToolBridgeExtensions
{
    /// <summary>
    /// Registers a web view with the tool bridge under a resource. The bridge drops the view when it closes.
    /// </summary>
    public static void RegisterWebView(
        this IDocumentWebViewToolBridge bridge,
        ResourceKey resource,
        IWebView webView)
    {
        bridge.Register(
            resource,
            webView.EvalAsync,
            webView.ReloadAsync,
            webView.CaptureScreenshotAsync);

        // A rename moves the registration, so the resource is read when the view closes.
        webView.Closing += (sender, e) => bridge.Unregister(webView.Resource);
    }
}
