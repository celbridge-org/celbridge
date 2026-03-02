using StreamJsonRpc;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// RPC service interface for document operations.
/// </summary>
public interface IHostDocument
{
    /// <summary>
    /// Loads the document content from the host.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentLoad)]
    Task<LoadResult> LoadAsync(LoadParams request);

    /// <summary>
    /// Saves the document content to the host.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentSave)]
    Task<SaveResult> SaveAsync(SaveParams request);

    /// <summary>
    /// Gets metadata about the current document.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentGetMetadata)]
    Task<DocumentMetadata> GetMetadataAsync(GetMetadataParams request);

    /// <summary>
    /// Saves binary content (base64 encoded) to the host.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentSaveBinary)]
    Task<SaveBinaryResult> SaveBinaryAsync(SaveBinaryParams request);

    /// <summary>
    /// Loads binary content (base64 encoded) from the host.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DocumentLoadBinary)]
    Task<LoadBinaryResult> LoadBinaryAsync(LoadBinaryParams request);
}
