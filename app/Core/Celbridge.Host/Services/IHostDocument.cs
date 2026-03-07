using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for document operations and document-related notifications.
/// </summary>
public interface IHostDocument
{
    /// <summary>
    /// Initializes the host connection with the WebView.
    /// Returns the document content, metadata, and localization strings.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.Initialize)]
    Task<InitializeResult> InitializeAsync(string protocolVersion);

    /// <summary>
    /// Loads the document content from the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentLoad)]
    Task<LoadResult> LoadAsync();

    /// <summary>
    /// Saves the document content to the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentSave)]
    Task<SaveResult> SaveAsync(string content);

    /// <summary>
    /// Called when the document content has changed in the WebView.
    /// Override to handle document changes.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentChanged)]
    void OnDocumentChanged() { }

    /// <summary>
    /// Called when an import operation completes in the WebView.
    /// Override to handle import completion.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.ImportComplete)]
    void OnImportComplete(bool success, string? error = null) { }

    /// <summary>
    /// Called when the JavaScript client has finished initializing and is ready for communication.
    /// Override to handle client ready notification.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.ClientReady)]
    void OnClientReady() { }
}
