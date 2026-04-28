namespace Celbridge.WebHost;

/// <summary>
/// Provides WebView-related services including URL classification.
/// </summary>
public interface IWebViewService
{
    /// <summary>
    /// Returns true if the URL is an external http/https URL suitable for navigating
    /// inside a .webview document. Local-scheme and relative-path URLs are rejected.
    /// </summary>
    bool IsExternalUrl(string url);

    /// <summary>
    /// Returns true if the WebViewDevTools feature flag is enabled in the
    /// user's .celbridge config. Callers that additionally need to block
    /// DevTools for a specific host (e.g. a bundled package that embeds
    /// sensitive material) should combine this with their own check.
    /// </summary>
    bool IsDevToolsFeatureEnabled();
}
