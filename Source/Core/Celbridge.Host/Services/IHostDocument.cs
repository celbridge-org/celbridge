using System.Text.Json.Serialization;
using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// Reason values passed with document/contentLoaded notifications so consumers can distinguish
/// the initial content load from subsequent reloads triggered by external file changes.
/// The enum is serialized as a JSON string via JsonStringEnumConverter; the exact wire strings
/// are declared via JsonStringEnumMemberName so the JSON format is decoupled from the identifiers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentLoadedReason>))]
public enum ContentLoadedReason
{
    /// <summary>
    /// Fired once after the initial content load when a document first opens.
    /// </summary>
    [JsonStringEnumMemberName("initial")]
    Initial,

    /// <summary>
    /// Fired each time the editor finishes processing an external file change (setValue plus
    /// any state restoration). Used by consumers that need to refresh dependent views once the
    /// reload cycle is fully complete.
    /// </summary>
    [JsonStringEnumMemberName("external-reload")]
    ExternalReload,
}

public static class DocumentRpcMethods
{
    public const string CurrentProtocolVersion = "1.0";

    public const string Initialize = "document/initialize";
    public const string Load = "document/load";
    public const string Save = "document/save";
    public const string Changed = "document/changed";
    public const string RequestSave = "document/requestSave";
    public const string ExternalChange = "document/externalChange";
    public const string ImportComplete = "document/importComplete";
    public const string ClientReady = "document/clientReady";
    public const string ContentLoaded = "document/contentLoaded";
    public const string RequestState = "document/requestState";
    public const string RestoreState = "document/restoreState";

    /// <summary>
    /// Validates the protocol version from the WebView client.
    /// Throws LocalRpcException if the version is not supported.
    /// </summary>
    public static void ValidateProtocolVersion(string protocolVersion)
    {
        if (protocolVersion != CurrentProtocolVersion)
        {
            throw new LocalRpcException(
                $"Unsupported protocol version: {protocolVersion}. Expected: {CurrentProtocolVersion}");
        }
    }
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
    /// Called every time the editor has finished loading (or reloading) content and is ready for edits.
    /// The reason parameter distinguishes the initial load from reloads triggered by external file changes.
    /// Defaults to Initial so older JS clients that send no payload continue to behave as before.
    /// </summary>
    [JsonRpcMethod(DocumentRpcMethods.ContentLoaded)]
    void OnContentLoaded(ContentLoadedReason reason = ContentLoadedReason.Initial) { }
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

    /// <summary>
    /// Requests the WebView to return its current editor state as an opaque JSON string.
    /// </summary>
    public static Task<string?> RequestStateAsync(this CelbridgeHost host)
        => host.Rpc.InvokeAsync<string?>(DocumentRpcMethods.RequestState);

    /// <summary>
    /// Sends previously saved editor state to the WebView for restoration.
    /// Returns when the WebView has acknowledged processing the state.
    /// </summary>
    public static Task RestoreStateAsync(this CelbridgeHost host, string state)
        => host.Rpc.InvokeAsync(DocumentRpcMethods.RestoreState, state);
}
