namespace Celbridge.Packages;

/// <summary>
/// The host's central catalog of established file types, loaded from the bundled file-types.json. Each
/// entry maps a file extension to the categories it belongs to on the Project Settings File Types page,
/// the language id an editor highlights it as, and the name the type is known by. An extension may
/// belong to several categories (for example JSON is both text and data). These are properties of the
/// extension, not of the editor that opens it. Packages describe their own novel extensions in their
/// manifests.
/// </summary>
public interface IFileTypeCatalog
{
    /// <summary>
    /// Loads the catalog from the bundled asset. Repeat calls are ignored, so a caller that needs the
    /// catalog populated can call this without coordinating with the others.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Returns the categories the given extension belongs to, or an empty list when the extension is not
    /// a catalogued established type. The extension includes its leading dot and is matched
    /// case-insensitively.
    /// </summary>
    IReadOnlyList<FileTypeCategory> GetCategories(string extension);

    /// <summary>
    /// Returns the language id a code editor highlights the extension as, or empty when the catalog
    /// assigns it no language. The host stores the value and never interprets it.
    /// </summary>
    string GetLanguage(string extension);

    /// <summary>
    /// Returns the name this file type is known by, or empty when the catalog names none.
    /// </summary>
    string GetDisplayName(string extension);

    /// <summary>
    /// Every extension the catalog assigns a language to, which is the set a general code editor claims.
    /// </summary>
    IReadOnlyList<string> LanguageExtensions { get; }
}
