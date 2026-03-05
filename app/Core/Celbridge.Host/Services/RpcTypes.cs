namespace Celbridge.Host;

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
