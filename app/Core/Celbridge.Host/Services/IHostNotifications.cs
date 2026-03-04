using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for handling notifications from JavaScript (no response expected).
/// </summary>
public interface IHostNotifications
{
    /// <summary>
    /// Called when the document content has changed in the WebView.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DocumentChanged)]
    void OnDocumentChanged();

    /// <summary>
    /// Called when a link is clicked in the WebView.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.LinkClicked)]
    void OnLinkClicked(string href);

    /// <summary>
    /// Called when an import operation completes in the WebView.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.ImportComplete)]
    void OnImportComplete(bool success, string? error = null);

    /// <summary>
    /// Called when the JavaScript client has finished initializing and is ready for communication.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.ClientReady)]
    void OnClientReady();

    /// <summary>
    /// Called when a keyboard shortcut is pressed in the WebView.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.KeyboardShortcut)]
    void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey);
}
