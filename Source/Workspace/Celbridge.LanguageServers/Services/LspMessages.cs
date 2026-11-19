using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// Reads and writes the language client's messages as JSON. A property is written in camel case, and a null
/// property is left out.
/// </summary>
internal static class LspJson
{
    public const string Version = "2.0";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns a message as JSON text.
    /// </summary>
    public static string Serialize(object message)
    {
        return JsonSerializer.Serialize(message, Options);
    }

    /// <summary>
    /// Reads a value from JSON. Returns false when the JSON is absent or has another shape.
    /// </summary>
    public static bool TryRead<T>(JsonNode? node, [NotNullWhen(true)] out T? value) where T : class
    {
        value = null;
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return false;
        }

        return value is not null;
    }
}

//
// The envelope of every message. Uno's key equality generator requires a record with an Id to be partial.
//

/// <summary>
/// A request, sent to a server.
/// </summary>
internal sealed partial record LspRequest(long Id, string Method, object? Params)
{
    public string Jsonrpc { get; } = LspJson.Version;
}

/// <summary>
/// A notification, sent to a server or to a page.
/// </summary>
internal sealed record LspNotification(string Method, object? Params)
{
    public string Jsonrpc { get; } = LspJson.Version;
}

/// <summary>
/// The answer to a request that succeeded. JSON-RPC requires the result, so a null result is written too.
/// </summary>
internal sealed partial record LspResponse(
    JsonNode Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? Result)
{
    public string Jsonrpc { get; } = LspJson.Version;
}

/// <summary>
/// The answer to a request that failed.
/// </summary>
internal sealed partial record LspErrorResponse(JsonNode Id, LspError Error)
{
    public string Jsonrpc { get; } = LspJson.Version;
}

/// <summary>
/// The error of a failed request.
/// </summary>
internal sealed record LspError(int Code, string? Message, JsonNode? Data = null);

/// <summary>
/// A message from a server or a page. A request has an id and a method. A notification has a method alone. A
/// response has an id, with a result or an error.
/// </summary>
internal sealed partial record LspIncomingMessage(
    JsonNode? Id,
    string? Method,
    JsonNode? Params,
    JsonNode? Result,
    LspError? Error);

//
// Messages from the language client to a server
//

internal sealed record ClientInfo(string Name, string Version);

internal sealed record WorkspaceFolder(string Uri, string Name);

/// <summary>
/// The parameters of the initialize request. The capabilities and the initialization options are JSON trees that
/// the language client passes on unread.
/// </summary>
internal sealed record InitializeParams(
    int ProcessId,
    ClientInfo ClientInfo,
    string RootUri,
    string RootPath,
    IReadOnlyList<WorkspaceFolder> WorkspaceFolders,
    JsonNode Capabilities,
    JsonNode? InitializationOptions);

internal sealed record TextDocumentIdentifier(string Uri);

internal sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

internal sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

internal sealed record DidOpenParams(TextDocumentItem TextDocument);

/// <summary>
/// The parameters of a change to a document. The changes are the page's own list, passed on unread.
/// </summary>
internal sealed record DidChangeParams(VersionedTextDocumentIdentifier TextDocument, JsonNode ContentChanges);

internal sealed record DidCloseParams(TextDocumentIdentifier TextDocument);

internal sealed record DiagnosticParams(
    TextDocumentIdentifier TextDocument,
    string? Identifier,
    string? PreviousResultId);

internal sealed partial record CancelParams(long Id);

//
// Messages from a server to the language client
//

internal sealed record InitializeResult(JsonObject? Capabilities);

/// <summary>
/// A server's capabilities. The language client reads only DiagnosticProvider, a boolean or an options object.
/// </summary>
internal sealed record ServerCapabilities(JsonNode? DiagnosticProvider);

internal sealed record DiagnosticProviderOptions(string? Identifier);

/// <summary>
/// The answer to a diagnostics request. The items are the document's diagnostics, passed on unread.
/// </summary>
internal sealed record DiagnosticReport(string? Kind, string? ResultId, JsonNode? Items);

internal sealed record DiagnosticCancellationData(bool? RetriggerRequest);

internal sealed record ConfigurationParams(IReadOnlyList<ConfigurationItem?>? Items);

internal sealed record ConfigurationItem(string? Section);

internal sealed record LogMessageParams(int Type, string? Message);

internal sealed record PublishDiagnosticsParams(string? Uri, JsonNode? Diagnostics);

//
// Messages between the language client and a page
//

internal sealed record PageDidOpenParams(string? Text, int Version);

internal sealed record PageDidChangeParams(int Version, JsonNode? ContentChanges);

internal sealed partial record PageCancelParams(JsonNode? Id);

internal sealed record ServerReadyParams(string ServerId, JsonNode Capabilities);

internal sealed record PageDiagnosticsParams(JsonNode Diagnostics);
