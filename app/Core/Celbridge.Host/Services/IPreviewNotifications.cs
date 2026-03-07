using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for handling notifications from preview JavaScript (no response expected).
/// Implement this interface to handle preview-related messages from the WebView.
/// </summary>
public interface IPreviewNotifications
{
    /// <summary>
    /// Called when the preview requests to open a local resource (e.g., a linked document).
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.PreviewOpenResource)]
    void OnOpenResource(string href);

    /// <summary>
    /// Called when the preview requests to open an external URL in the browser.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.PreviewOpenExternal)]
    void OnOpenExternal(string href);

    /// <summary>
    /// Called when the preview requests to sync the editor scroll position.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.PreviewSyncToEditor)]
    void OnSyncToEditor(double scrollPercentage);
}
