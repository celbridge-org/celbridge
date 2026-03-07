using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// Interface for rendering preview content in a WebView.
/// Implementations provide preview functionality for specific file types (e.g., Markdown, AsciiDoc).
/// </summary>
public interface IPreviewRenderer
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
    /// Sets the document context for resolving relative paths.
    /// Called after navigation completes and before the first preview update.
    /// </summary>
    Task SetDocumentContextAsync(CoreWebView2 webView, string documentPath, string projectFolderPath);

    /// <summary>
    /// Updates the preview with new content.
    /// Called whenever the editor content changes.
    /// </summary>
    Task UpdatePreviewAsync(CoreWebView2 webView, string content);

    /// <summary>
    /// Scrolls the preview to a specific position.
    /// </summary>
    Task ScrollToPositionAsync(CoreWebView2 webView, double scrollPercentage);

    /// <summary>
    /// Handles messages received from the preview WebView.
    /// Return true if the message was handled, false otherwise.
    /// </summary>
    bool HandlePreviewMessage(string messageType, System.Text.Json.JsonElement messageData, Action<string> openLocalResource, Action<string> openExternalUrl);
}
