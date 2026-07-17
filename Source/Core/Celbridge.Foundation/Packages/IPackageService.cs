using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// Information about a document type provided by a package.
/// </summary>
public record DocumentTypeInfo(
    string DisplayName,
    IReadOnlyList<string> FileExtensions);

/// <summary>
/// Provides package discovery, editor instance resolution, document type information, and
/// template content.
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// Discovers all packages (bundled module packages and project packages), resolves the
    /// project's declared editor instances against the activated packages, and registers all
    /// package behaviors (e.g. custom document editor factories).
    /// </summary>
    Task RegisterPackagesAsync(string projectFolderPath);

    /// <summary>
    /// Re-runs project-package discovery against the on-disk state, refreshing the discovered packages and
    /// the load failures. Does not fire PackagesInitializedMessage, rewrite the project load report, or
    /// re-register editor contributions.
    /// </summary>
    Task RescanProjectPackagesAsync(string projectFolderPath);

    /// <summary>
    /// Gets document type entries for the available editors (declared instances and built-ins)
    /// that declare templates.
    /// </summary>
    IReadOnlyList<DocumentTypeInfo> GetDocumentTypes();

    /// <summary>
    /// Returns all discovered packages from both bundled and project sources, including
    /// discovered-but-inactive packages.
    /// </summary>
    IReadOnlyList<Package> GetAllPackages();

    /// <summary>
    /// Returns the package load failures from the most recent discovery pass.
    /// Empty before the first discovery.
    /// </summary>
    IReadOnlyList<PackageLoadFailure> GetLoadFailures();

    /// <summary>
    /// Returns all editor contributions from all discovered packages.
    /// </summary>
    IReadOnlyList<EditorContribution> GetAllEditors();

    /// <summary>
    /// Returns the project's declared editor instances, in declaration order. Only instances
    /// that resolved to an activated package and a known contribution are included.
    /// </summary>
    IReadOnlyList<EditorInstance> GetEditorInstances();

    /// <summary>
    /// Returns the built-in editors served from the always-active packages, in host catalog
    /// order. An optional built-in whose package is not present is omitted.
    /// </summary>
    IReadOnlyList<EditorInstance> GetBuiltInEditors();

    /// <summary>
    /// Returns the package that provides the declared instance or built-in editor with the
    /// specified id, or null if no such editor is registered.
    /// </summary>
    Package? GetContributingPackage(EditorInstanceId editorId);

    /// <summary>
    /// Gets the default template content for a file extension, or null if no available editor
    /// provides a default template for that extension.
    /// </summary>
    byte[]? GetDefaultTemplateContent(string fileExtension);

    /// <summary>
    /// Reads the seed template bytes for a utility contribution from its manifest template path.
    /// Returns an empty array when the utility declares no template, and null when a declared
    /// template file is missing or unreadable.
    /// </summary>
    byte[]? GetUtilityTemplateContent(EditorContribution contribution);
}
