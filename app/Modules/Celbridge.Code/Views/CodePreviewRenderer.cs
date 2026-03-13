using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// A generic ICodePreviewRenderer implementation for code editors with a preview panel.
/// </summary>
public class CodePreviewRenderer : ICodePreviewRenderer
{
    private readonly string _hostName;
    private readonly string _extensionDirectory;

    public string PreviewHostName { get; }

    public string PreviewAssetFolder { get; }

    public string PreviewPageUrl { get; }

    public CodePreviewRenderer(
        string hostName,
        string extensionDirectory,
        string previewHostName,
        string previewAssetFolder,
        string previewPageUrl)
    {
        _hostName = hostName;
        _extensionDirectory = extensionDirectory;

        PreviewHostName = previewHostName;
        PreviewAssetFolder = Path.Combine(extensionDirectory, previewAssetFolder);
        PreviewPageUrl = previewPageUrl;
    }

    public Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath)
    {
        // Map the extension's own assets
        webView.SetVirtualHostNameToFolderMapping(
            _hostName,
            _extensionDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        // Map the project folder for local resource resolution
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
