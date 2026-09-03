namespace Celbridge.Packages;

/// <summary>
/// The prefixed name of the icon a file type is drawn with, the hex colour it is drawn in, and an
/// optional scale relative to the host's icon size. An empty colour leaves the icon in the theme's
/// default colour; a scale of 1.0 draws it at the host's size, and a larger value enlarges a glyph its
/// font draws small within its em box.
/// </summary>
public sealed record FileTypeIcon(string IconName, string Color, double Scale = 1.0);

/// <summary>
/// The host's central record of established file types, loaded from the bundled file-types.json. Entries
/// describe the extension itself, so an extension is described here once however many editors claim it.
/// </summary>
public interface IFileTypeCatalog
{
    /// <summary>
    /// Loads the catalog from the bundled asset. Repeat calls are ignored, so a caller that needs the
    /// catalog populated can call this without coordinating with the others.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Returns true when the catalog records the extension as a binary format. False for a text format
    /// and for an extension the catalog does not know. The extension includes its leading dot and is
    /// matched case-insensitively.
    /// </summary>
    bool IsBinaryExtension(string extension);

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
    /// Returns the icon the catalog assigns the extension, or null when it assigns none.
    /// </summary>
    FileTypeIcon? GetIcon(string extension);

    /// <summary>
    /// Returns the icon the catalog assigns to a whole file name, or null when it assigns none. Covers
    /// the files that carry no usable extension, such as Makefile and LICENSE. Matched
    /// case-insensitively.
    /// </summary>
    FileTypeIcon? GetIconForFileName(string fileName);

    /// <summary>
    /// Every extension the catalog assigns a coding language to, which is the set a general code editor claims.
    /// </summary>
    IReadOnlyList<string> LanguageExtensions { get; }

    /// <summary>
    /// Every extension the catalog assigns an icon to.
    /// </summary>
    IReadOnlyList<string> IconExtensions { get; }

    /// <summary>
    /// Every whole file name the catalog assigns an icon to.
    /// </summary>
    IReadOnlyList<string> IconFileNames { get; }
}
