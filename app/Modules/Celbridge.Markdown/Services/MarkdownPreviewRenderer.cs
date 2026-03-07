using Celbridge.Code.Views;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Preview renderer for Markdown files.
/// Uses marked.js to render Markdown to HTML in a WebView.
/// </summary>
public class MarkdownPreviewRenderer : IPreviewRenderer
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

    public async Task SetDocumentContextAsync(CoreWebView2 webView, string documentPath, string projectFolderPath)
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

        // Escape the path for JavaScript
        var escapedPath = basePath.Replace("\\", "\\\\").Replace("`", "\\`");
        var script = $"window.celbridge.setBasePath(`{escapedPath}`);";
        await webView.ExecuteScriptAsync(script);
    }

    public async Task UpdatePreviewAsync(CoreWebView2 webView, string content)
    {
        // Escape the content for JavaScript template literal
        var escapedContent = EscapeForJavaScript(content);
        var script = $"window.celbridge.updatePreview(`{escapedContent}`);";
        await webView.ExecuteScriptAsync(script);
    }

    public async Task ScrollToPositionAsync(CoreWebView2 webView, double scrollPercentage)
    {
        var script = $"window.celbridge.scrollToPosition({scrollPercentage});";
        await webView.ExecuteScriptAsync(script);
    }

    public bool HandlePreviewMessage(
        string messageType,
        JsonElement messageData,
        Action<string> openLocalResource,
        Action<string> openExternalUrl)
    {
        switch (messageType)
        {
            case "openResource":
                {
                    if (messageData.TryGetProperty("href", out var hrefElement))
                    {
                        var href = hrefElement.GetString();
                        if (!string.IsNullOrEmpty(href))
                        {
                            openLocalResource(href);
                            return true;
                        }
                    }
                }
                break;

            case "openExternal":
                {
                    if (messageData.TryGetProperty("href", out var hrefElement))
                    {
                        var href = hrefElement.GetString();
                        if (!string.IsNullOrEmpty(href))
                        {
                            openExternalUrl(href);
                            return true;
                        }
                    }
                }
                break;
        }

        return false;
    }

    private static string EscapeForJavaScript(string content)
    {
        return content
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("$", "\\$");
    }
}
