namespace Celbridge.Extensions;

/// <summary>
/// Information about a document type provided by an extension.
/// </summary>
public record DocumentTypeInfo(
    string DisplayName,
    IReadOnlyList<string> FileExtensions);

/// <summary>
/// Provides extension discovery, document type information, and template content.
/// </summary>
public interface IExtensionService
{
    /// <summary>
    /// Discovers all extensions (bundled module extensions and project extensions)
    /// and registers all extension behaviors (e.g. custom document editor factories).
    /// </summary>
    void Initialize(string projectFolderPath);

    /// <summary>
    /// Gets document type entries from discovered extensions that declare templates.
    /// Extensions with a disabled feature flag are excluded from the results.
    /// </summary>
    IReadOnlyList<DocumentTypeInfo> GetDocumentTypes();

    /// <summary>
    /// Returns all discovered extensions from both bundled and project sources.
    /// </summary>
    IReadOnlyList<Extension> GetAllExtensions();

    /// <summary>
    /// Returns all document editor contributions from all discovered extensions.
    /// </summary>
    IReadOnlyList<DocumentContribution> GetAllDocumentEditors();

    /// <summary>
    /// Gets the default template content for a file extension, if provided by an extension.
    /// Returns null if no extension provides a default template for this extension.
    /// </summary>
    byte[]? GetDefaultTemplateContent(string fileExtension);
}
