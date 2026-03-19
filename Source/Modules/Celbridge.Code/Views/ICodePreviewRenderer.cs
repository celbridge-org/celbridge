using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// Interface for rendering preview content in a WebView.
/// Implementations provide preview functionality for specific file types (e.g., Markdown, AsciiDoc).
/// Preview renderers use JSON-RPC via CelbridgeHost for communication with the WebView.
/// </summary>
public interface ICodePreviewRenderer
{
    /// <summary>
    /// The full URL to the preview page (e.g., "https://ext-celbridge-notes.celbridge/preview/index.html").
    /// </summary>
    string PreviewPageUrl { get; }

    /// <summary>
    /// Configures the WebView for preview rendering.
    /// Called once during preview initialization, before navigation.
    /// Implementations should set up virtual host mappings for both the preview assets
    /// and the project folder.
    /// </summary>
    Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath);
}
