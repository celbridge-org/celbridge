using Celbridge.Extensions;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// A generic IPreviewRenderer implementation configured from an extension manifest.
/// Used when a code extension declares a preview field in its editor.json.
/// </summary>
public class ExtensionPreviewRenderer : IPreviewRenderer
{
    private readonly ExtensionManifest _manifest;
    private readonly CodePreviewConfig _previewConfig;

    public string PreviewHostName => _previewConfig.HostName;

    public string PreviewAssetFolder { get; }

    public string PreviewPageUrl => _previewConfig.PageUrl;

    public ExtensionPreviewRenderer(ExtensionManifest manifest)
    {
        _manifest = manifest;

        _previewConfig = manifest.CodePreview
            ?? throw new ArgumentException("Extension manifest does not have preview configuration", nameof(manifest));

        // Resolve the asset folder relative to the extension directory
        PreviewAssetFolder = Path.Combine(manifest.ExtensionDirectory, _previewConfig.AssetFolder);
    }

    public Task ConfigureWebViewAsync(CoreWebView2 webView, string projectFolderPath)
    {
        // Map the extension's own assets
        webView.SetVirtualHostNameToFolderMapping(
            _manifest.HostName,
            _manifest.ExtensionDirectory,
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
