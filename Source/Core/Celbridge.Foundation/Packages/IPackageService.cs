namespace Celbridge.Packages;

/// <summary>
/// Information about a document type provided by a package.
/// </summary>
public record DocumentTypeInfo(
    string DisplayName,
    IReadOnlyList<string> FileExtensions);

/// <summary>
/// Provides package discovery, document type information, and template content.
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// Discovers all packages (bundled module packages and project packages)
    /// and registers all package behaviors (e.g. custom document editor factories).
    /// </summary>
    void RegisterPackages(string projectFolderPath);

    /// <summary>
    /// Gets document type entries from discovered packages that declare templates.
    /// Packages with a disabled feature flag are excluded from the results.
    /// </summary>
    IReadOnlyList<DocumentTypeInfo> GetDocumentTypes();

    /// <summary>
    /// Returns all discovered packages from both bundled and project sources.
    /// </summary>
    IReadOnlyList<Package> GetAllPackages();

    /// <summary>
    /// Returns all document editor contributions from all discovered packages.
    /// </summary>
    IReadOnlyList<DocumentEditorContribution> GetAllDocumentEditors();

    /// <summary>
    /// Gets the default template content for a file extension, if provided by a package.
    /// Returns null if no package provides a default template for this extension.
    /// </summary>
    byte[]? GetDefaultTemplateContent(string fileExtension);
}
