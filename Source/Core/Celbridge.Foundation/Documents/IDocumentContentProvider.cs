namespace Celbridge.Documents;

/// <summary>
/// Generates document content on the host side for a given file resource.
/// Used by contribution editors that render derived content (e.g., an HTML preview
/// produced from a parsed source file) rather than surfacing the raw file bytes.
/// </summary>
public interface IDocumentContentProvider
{
    /// <summary>
    /// Indicates whether this provider can supply content for the specified file resource.
    /// Typically matches on the file extension.
    /// </summary>
    bool CanHandle(ResourceKey fileResource);

    /// <summary>
    /// Generates the content string that will be returned to the contribution editor
    /// via the InitializeAsync / LoadAsync RPC path. The returned string is treated
    /// as opaque by the host and is delivered verbatim to the editor's onContent callback.
    /// </summary>
    Task<Result<string>> LoadContentAsync(ResourceKey fileResource);
}
