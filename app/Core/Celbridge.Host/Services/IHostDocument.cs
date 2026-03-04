using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for document operations.
/// </summary>
public interface IHostDocument
{
    /// <summary>
    /// Initializes the host connection with the WebView.
    /// Returns the document content, metadata, and localization strings.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.Initialize)]
    Task<InitializeResult> InitializeAsync(string protocolVersion);

    /// <summary>
    /// Loads the document content from the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DocumentLoad)]
    Task<LoadResult> LoadAsync();

    /// <summary>
    /// Saves the document content to the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DocumentSave)]
    Task<SaveResult> SaveAsync(string content);
}
