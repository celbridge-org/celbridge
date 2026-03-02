using StreamJsonRpc;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// RPC service interface for handling notifications from JavaScript (no response expected).
/// </summary>
public interface IHostNotifications
{
    /// <summary>
    /// Called when the document content has changed in the WebView.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentChanged)]
    void OnDocumentChanged();

    /// <summary>
    /// Called when a link is clicked in the WebView.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.LinkClicked)]
    void OnLinkClicked(LinkClickedParams request);

    /// <summary>
    /// Called when an import operation completes in the WebView.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.ImportComplete)]
    void OnImportComplete(ImportCompleteNotification notification);
}
