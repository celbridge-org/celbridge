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
    public string PreviewHostName => "markdown-preview.celbridge";

    public string PreviewAssetFolder => "Celbridge.Markdown/Web/markdown-preview";

    public string PreviewPageUrl => "https://markdown-preview.celbridge/index.html";

    public Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath)
    {
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

    public string ComputeBasePath(string documentPath, string projectFolderPath)
    {
        // Get the document's directory path relative to the project root
        var documentDir = Path.GetDirectoryName(documentPath);

        var basePath = string.Empty;
        if (!string.IsNullOrEmpty(documentDir) && !string.IsNullOrEmpty(projectFolderPath))
        {
            if (documentDir.StartsWith(projectFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                basePath = documentDir.Substring(projectFolderPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        return basePath;
    }
}
