using StreamJsonRpc;

namespace Celbridge.Host;

public static class DocumentRpcMethods
{
    public const string Initialize = "document/initialize";
    public const string Load = "document/load";
    public const string Save = "document/save";
    public const string Changed = "document/changed";
    public const string RequestSave = "document/requestSave";
    public const string ExternalChange = "document/externalChange";
    public const string ImportComplete = "document/importComplete";
    public const string ClientReady = "document/clientReady";
    public const string ContentLoaded = "document/contentLoaded";
}

/// <summary>
/// RPC service interface for document operations and document-related notifications.
/// </summary>
public interface IHostDocument
{
    /// <summary>
    /// Initializes the host connection with the WebView.
    /// Returns the document content, metadata, and localization strings.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.Initialize)]
    Task<InitializeResult> InitializeAsync(string protocolVersion);

    /// <summary>
    /// Loads the document content from the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.Load)]
    Task<LoadResult> LoadAsync();

    /// <summary>
    /// Saves the document content to the host.
    /// Content is text for text documents or base64-encoded for binary documents.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.Save)]
    Task<SaveResult> SaveAsync(string content);

    /// <summary>
    /// Called when the document content has changed in the WebView.
    /// Override to handle document changes.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.Changed)]
    void OnDocumentChanged() { }

    /// <summary>
    /// Called when an import operation completes in the WebView.
    /// Override to handle import completion.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.ImportComplete)]
    void OnImportComplete(bool success, string? error = null) { }

    /// <summary>
    /// Called when the JavaScript client has finished initializing and is ready for communication.
    /// Override to handle client ready notification.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.ClientReady)]
    void OnClientReady() { }

    /// <summary>
    /// Called when document content has been loaded and the editor is ready for edits.
    /// Override to handle content loaded notification.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.ContentLoaded)]
    void OnContentLoaded() { }
}

public static class HostDocumentExtensions
{
    /// <summary>
    /// Requests the WebView to save the current document content.
    /// JS should respond by calling document/save.
    /// </summary>
    public static Task NotifyRequestSaveAsync(this CelbridgeHost host)
        => host.Rpc.NotifyAsync(DocumentRpcMethods.RequestSave);

    /// <summary>
    /// Notifies the WebView that the document has been externally modified.
    /// </summary>
    public static Task NotifyExternalChangeAsync(this CelbridgeHost host)
        => host.Rpc.NotifyAsync(DocumentRpcMethods.ExternalChange);
}
