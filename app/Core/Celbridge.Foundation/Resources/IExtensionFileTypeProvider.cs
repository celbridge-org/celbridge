namespace Celbridge.Resources;

/// <summary>
/// Information about a file type provided by an extension.
/// </summary>
public record ExtensionFileTypeInfo(
    string DisplayName,
    string Extension,
    string? FeatureFlag);

/// <summary>
/// Provides file type information and template content from discovered extension manifests.
/// This abstraction allows services that don't reference the Documents project
/// to access extension-provided file types and templates.
/// </summary>
public interface IExtensionFileTypeProvider
{
    /// <summary>
    /// Gets file type entries from discovered extension manifests that declare templates.
    /// Each entry represents one extension (file type) with its display name and extension.
    /// </summary>
    IReadOnlyList<ExtensionFileTypeInfo> GetExtensionFileTypes();

    /// <summary>
    /// Gets the default template content for a file extension, if provided by an extension.
    /// Returns null if no extension provides a default template for this extension.
    /// </summary>
    byte[]? GetDefaultTemplateContent(string fileExtension);
}
