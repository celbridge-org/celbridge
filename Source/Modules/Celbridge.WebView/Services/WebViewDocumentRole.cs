namespace Celbridge.WebView.Services;

/// <summary>
/// Identifies the trust posture and content source of a WebViewDocumentView instance.
/// External-URL .webview tabs and the project HTML viewer share the same view class
/// but differ in navigation policy.
/// </summary>
public enum WebViewDocumentRole
{
    /// <summary>
    /// Hosts an external http/https URL configured in a .webview document.
    /// </summary>
    ExternalUrl,

    /// <summary>
    /// Hosts a project-served .html or .htm file via the project.celbridge virtual host.
    /// </summary>
    HtmlViewer,
}
