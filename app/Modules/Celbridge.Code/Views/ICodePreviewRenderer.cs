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
    /// The virtual host name for preview assets (e.g., "markdown-preview.celbridge").
    /// </summary>
    string PreviewHostName { get; }

    /// <summary>
    /// The folder path containing preview assets, relative to the application root
    /// (e.g., "Celbridge.Markdown/Web/markdown-preview").
    /// </summary>
    string PreviewAssetFolder { get; }

    /// <summary>
    /// The full URL to the preview page (e.g., "https://markdown-preview.celbridge/index.html").
    /// </summary>
    string PreviewPageUrl { get; }

    /// <summary>
    /// Configures the WebView for preview rendering.
    /// Called once during preview initialization, before navigation.
    /// Use this to set up additional virtual host mappings or other WebView configuration.
    /// </summary>
    Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath);

    /// <summary>
    /// Computes the base path for resolving relative resources in the preview.
    /// This path is sent to the preview JavaScript via JSON-RPC.
    /// </summary>
    string ComputeBasePath(string documentPath, string projectFolderPath);
}
