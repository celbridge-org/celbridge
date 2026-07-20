namespace Celbridge.Packages;

/// <summary>
/// The host's central catalog of established file types, mapping a file extension to the categories it
/// belongs to on the Project Settings File Types page. An extension may belong to several categories
/// (for example JSON is both text and data). Categories are a property of the extension, not of the
/// editor that opens it. Packages declare categories for their own novel extensions in their manifests.
/// </summary>
public interface IFileTypeCatalog
{
    /// <summary>
    /// Returns the categories the given extension belongs to, or an empty list when the extension is not
    /// a catalogued established type. The extension includes its leading dot and is matched
    /// case-insensitively.
    /// </summary>
    IReadOnlyList<FileTypeCategory> GetCategories(string extension);
}
