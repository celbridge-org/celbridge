namespace Celbridge.Host;

// =============================================================================
// Initialize Result
// =============================================================================

/// <summary>
/// Metadata about the document being edited.
/// Includes the locale for JS-side localization loading.
/// </summary>
public record DocumentMetadata(string FilePath, string ResourceKey, string FileName, string Locale);

/// <summary>
/// Result of the document/initialize request.
/// Localization is handled by JS fetching from the extension's localization folder.
/// WritableState is the string name of the document's writable state at handshake time.
/// </summary>
public record InitializeResult(string Content, DocumentMetadata Metadata, string WritableState, string? EditorStateJson = null);

/// <summary>
/// The host capability context for a contribution editor: the resolved tool allowlist,
/// the package's secrets, and its options. Delivered to the WebView client either as the
/// pre-injected window.__celbridgeContext global (packaged WinUI head) or over the bridge
/// via host/getContext (Skia head, where document-start script injection is unavailable).
/// </summary>
public record CelbridgeContext(
    IReadOnlyList<string> PermittedTools,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyDictionary<string, string> Options);

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
