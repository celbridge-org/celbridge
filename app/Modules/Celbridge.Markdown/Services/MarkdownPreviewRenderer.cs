using Celbridge.Code.Views;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Preview renderer for Markdown files.
/// Uses marked.js to render Markdown to HTML in a WebView.
/// Communication with the WebView is handled via JSON-RPC through CelbridgeHost.
/// </summary>
public class MarkdownPreviewRenderer : ICodePreviewRenderer
{
    private const string HostName = "markdown-preview.celbridge";

    private const string AssetFolder = "Celbridge.Markdown/Web/markdown-preview";

    public string PreviewPageUrl => $"https://{HostName}/index.html";

    public Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath)
    {
        // Map the Markdown preview assets
        webView.SetVirtualHostNameToFolderMapping(
            HostName,
            AssetFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        // Map the project folder so local image paths resolve correctly
        if (!string.IsNullOrEmpty(projectFolderPath))
        {
            webView.SetVirtualHostNameToFolderMapping(
                "project.celbridge",
                projectFolderPath,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        return Task.CompletedTask;
    }
}
