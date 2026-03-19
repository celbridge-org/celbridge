using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// A generic ICodePreviewRenderer for extension-based code editors with a preview panel.
/// Maps the extension directory to the extension's virtual host so preview assets are
/// served alongside other extension resources.
/// </summary>
public class CodePreviewRenderer(
    string extensionHostName,
    string extensionFolder,
    string previewEntryPoint) : ICodePreviewRenderer
{
    public string PreviewPageUrl { get; } = $"https://{extensionHostName}/{previewEntryPoint}";

    public Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath)
    {
        // Map the extension directory to the extension's virtual host.
        // Preview assets are served from here alongside other extension resources.
        webView.SetVirtualHostNameToFolderMapping(
            extensionHostName,
            extensionFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        // Map the project folder for local resource resolution (e.g., images in Markdown)
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
