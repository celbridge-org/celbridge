using Celbridge.Documents;

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
    Task RegisterPackagesAsync(string projectFolderPath);

    /// <summary>
    /// Re-runs project-package discovery against the on-disk state and refreshes
    /// the load report, without firing PackagesInitializedMessage. Lets a
    /// session-mid caller (such as package_status) see packages added or
    /// removed after the workspace loaded. Editor-contribution registration is
    /// workspace-load-scoped and is not refreshed by this call.
    /// </summary>
    Task RescanProjectPackagesAsync(string projectFolderPath);

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
    /// Returns the package load failures from the most recent discovery pass,
    /// so a status query can surface them after the load-time error banner has
    /// fired. Empty before the first discovery.
    /// </summary>
    IReadOnlyList<PackageLoadFailure> GetLoadFailures();

    /// <summary>
    /// Returns all document editor contributions from all discovered packages.
    /// </summary>
    IReadOnlyList<EditorContribution> GetAllDocumentEditors();

    /// <summary>
    /// Returns the editor instances derived from the discovered contributions. Each contribution
    /// yields one instance whose id is the composed "{packageName}.{contributionId}" id.
    /// </summary>
    IReadOnlyList<EditorInstance> GetEditorInstances();

    /// <summary>
    /// Returns the package that contributes the editor instance with the
    /// specified instance ID, or null if no contributing package is registered.
    /// Instance IDs follow the "{packageName}.{contributionId}" format.
    /// </summary>
    Package? GetContributingPackage(EditorInstanceId editorId);

    /// <summary>
    /// Gets the default template content for a file extension, if provided by a package.
    /// Returns null if no package provides a default template for this extension.
    /// </summary>
    byte[]? GetDefaultTemplateContent(string fileExtension);

    /// <summary>
    /// Reads the seed template bytes for a utility contribution from its manifest template path.
    /// Returns an empty array when the utility declares no template, and null when a declared
    /// template file is missing or unreadable.
    /// </summary>
    byte[]? GetUtilityTemplateContent(EditorContribution contribution);
}
