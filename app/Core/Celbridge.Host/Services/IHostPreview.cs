using StreamJsonRpc;

namespace Celbridge.Host;

public static class PreviewRpcMethods
{
    // Host-to-client notifications
    public const string SetContext = "preview/setContext";
    public const string Update = "preview/update";
    public const string Scroll = "preview/scroll";

    // Client-to-host notifications
    public const string OpenResource = "preview/openResource";
    public const string OpenExternal = "preview/openExternal";
    public const string SyncToEditor = "preview/syncToEditor";
}

/// <summary>
/// RPC service interface for handling notifications from preview JavaScript (no response expected).
/// Implement this interface to handle preview-related messages from the WebView.
/// </summary>
public interface IHostPreview
{
    /// <summary>
    /// Called when the preview requests to open a local resource (e.g., a linked document).
    /// </summary>
    [JsonRpcMethod(PreviewRpcMethods.OpenResource)]
    void OnOpenResource(string href);

    /// <summary>
    /// Called when the preview requests to open an external URL in the browser.
    /// </summary>
    [JsonRpcMethod(PreviewRpcMethods.OpenExternal)]
    void OnOpenExternal(string href);

    /// <summary>
    /// Called when the preview requests to sync the editor scroll position.
    /// </summary>
    [JsonRpcMethod(PreviewRpcMethods.SyncToEditor)]
    void OnSyncToEditor(double scrollPercentage);
}

public static class HostPreviewExtensions
{
    /// <summary>
    /// Sets the document context for the preview pane (base path for resolving relative resources).
    /// </summary>
    public static Task NotifyPreviewSetContextAsync(this CelbridgeHost host, string basePath)
        => host.Rpc.NotifyWithParameterObjectAsync(PreviewRpcMethods.SetContext, new { basePath });

    /// <summary>
    /// Updates the preview content.
    /// </summary>
    public static Task NotifyPreviewUpdateAsync(this CelbridgeHost host, string content)
        => host.Rpc.NotifyWithParameterObjectAsync(PreviewRpcMethods.Update, new { content });

    /// <summary>
    /// Scrolls the preview to a specific position (0.0 to 1.0).
    /// </summary>
    public static Task NotifyPreviewScrollAsync(this CelbridgeHost host, double scrollPercentage)
        => host.Rpc.NotifyWithParameterObjectAsync(PreviewRpcMethods.Scroll, new { scrollPercentage });
}
