namespace Celbridge.Host;

/// <summary>
/// JSON-RPC error codes as defined in the specification.
/// See: https://www.jsonrpc.org/specification
/// </summary>
public static class JsonRpcErrorCodes
{
    /// <summary>Invalid JSON was received.</summary>
    public const int ParseError = -32700;

    /// <summary>The JSON sent is not a valid Request object.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The method does not exist or is not available.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Invalid method parameter(s).</summary>
    public const int InvalidParams = -32602;

    /// <summary>Internal JSON-RPC error.</summary>
    public const int InternalError = -32603;

    /// <summary>Unsupported protocol version.</summary>
    public const int InvalidVersion = -32001;
}

/// <summary>
/// Exception that maps to JSON-RPC errors for WebView2 RPC communication.
/// </summary>
public class HostRpcException : Exception
{
    public int Code { get; }
    public new object? Data { get; }

    public HostRpcException(int code, string message, object? data = null) : base(message)
    {
        Code = code;
        Data = data;
    }
}

// =============================================================================
// Initialize Result
// =============================================================================

/// <summary>
/// Metadata about the document being edited.
/// </summary>
public record DocumentMetadata(string FilePath, string ResourceKey, string FileName);

/// <summary>
/// Result of the bridge/initialize request.
/// </summary>
public record InitializeResult(
    string Content,
    DocumentMetadata Metadata,
    Dictionary<string, string> Localization);

// =============================================================================
// Document Operation Results
// =============================================================================

/// <summary>
/// Result of the document/load request.
/// Content is text for text documents or base64-encoded for binary documents.
/// </summary>
public record LoadResult(string Content, DocumentMetadata Metadata);

/// <summary>
/// Result of the document/save request.
/// </summary>
public record SaveResult(bool Success, string? Error = null);

// =============================================================================
// Dialog Operation Results
// =============================================================================

/// <summary>
/// Result of the dialog/pickImage request.
/// </summary>
public record PickImageResult(string? Path);

/// <summary>
/// Result of the dialog/pickFile request.
/// </summary>
public record PickFileResult(string? Path);

/// <summary>
/// Result of the dialog/alert request.
/// </summary>
public record AlertResult();

// =============================================================================
// Outbound Notification Types (C# to JS)
// =============================================================================

/// <summary>
/// Notification sent when localization is updated (C# to JS).
/// </summary>
public record LocalizationUpdatedNotification(Dictionary<string, string> Strings);

// =============================================================================
// Inbound Parameter Types (JS to C#)
// =============================================================================

/// <summary>
/// Parameters for the bridge/initialize request.
/// </summary>
public record InitializeParams(string ProtocolVersion);

/// <summary>
/// Parameters for the document/save request.
/// </summary>
public record SaveParams(string Content);

/// <summary>
/// Parameters for the dialog/pickImage request.
/// </summary>
public record PickImageParams(string[] Extensions);

/// <summary>
/// Parameters for the dialog/pickFile request.
/// </summary>
public record PickFileParams(string[] Extensions);

/// <summary>
/// Parameters for the dialog/alert request.
/// </summary>
public record AlertParams(string Title, string Message);

/// <summary>
/// Parameters for the link/clicked notification (JS to C#).
/// </summary>
public record LinkClickedParams(string Href);

/// <summary>
/// Notification sent when document content changes (JS to C#).
/// </summary>
public record DocumentChangedNotification();

/// <summary>
/// Notification sent when import operation completes (JS to C#).
/// </summary>
public record ImportCompleteNotification(bool Success, string? Error = null);
