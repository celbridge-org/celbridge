namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// JSON-RPC error codes as defined in the specification.
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
/// Base class for bridge exceptions that map to JSON-RPC errors.
/// </summary>
public class BridgeException : Exception
{
    public int Code { get; }
    public object? Data { get; }

    public BridgeException(int code, string message, object? data = null) : base(message)
    {
        Code = code;
        Data = data;
    }
}

// =============================================================================
// Initialize Request/Response
// =============================================================================

/// <summary>
/// Parameters for the bridge/initialize request.
/// </summary>
public record InitializeParams(string ProtocolVersion);

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
    Dictionary<string, string> Localization,
    ThemeInfo Theme);

/// <summary>
/// Theme information sent to the WebView.
/// </summary>
public record ThemeInfo(string Name, bool IsDark);

// =============================================================================
// Document Operations
// =============================================================================

/// <summary>
/// Parameters for the document/load request.
/// </summary>
public record LoadParams(bool IncludeMetadata = false);

/// <summary>
/// Result of the document/load request.
/// </summary>
public record LoadResult(string Content, DocumentMetadata? Metadata = null);

/// <summary>
/// Parameters for the document/save request.
/// </summary>
public record SaveParams(string Content);

/// <summary>
/// Result of the document/save request.
/// </summary>
public record SaveResult(bool Success, string? Error = null);

/// <summary>
/// Parameters for the document/getMetadata request.
/// </summary>
public record GetMetadataParams();

// =============================================================================
// Dialog Operations
// =============================================================================

/// <summary>
/// Parameters for the dialog/pickImage request.
/// </summary>
public record PickImageParams(string[] Extensions);

/// <summary>
/// Result of the dialog/pickImage request.
/// </summary>
public record PickImageResult(string? Path);

/// <summary>
/// Parameters for the dialog/pickFile request.
/// </summary>
public record PickFileParams(string[] Extensions);

/// <summary>
/// Result of the dialog/pickFile request.
/// </summary>
public record PickFileResult(string? Path);

/// <summary>
/// Parameters for the dialog/alert request.
/// </summary>
public record AlertParams(string Title, string Message);

/// <summary>
/// Result of the dialog/alert request.
/// </summary>
public record AlertResult();

// =============================================================================
// Notifications (no response expected)
// =============================================================================

/// <summary>
/// Notification sent when document content changes.
/// </summary>
public record DocumentChangedNotification();

/// <summary>
/// Notification sent when external file changes are detected.
/// </summary>
public record ExternalChangeNotification();

/// <summary>
/// Notification sent when theme changes.
/// </summary>
public record ThemeChangedNotification(ThemeInfo Theme);

/// <summary>
/// Notification sent when localization is updated.
/// </summary>
public record LocalizationUpdatedNotification(Dictionary<string, string> Strings);
