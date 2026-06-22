namespace Celbridge.UserInterface;

/// <summary>
/// Information required to display a file-type icon using the FontIcon control.
/// </summary>
public record FileIconDefinition(string FontCharacter, string FontColor, string FontFamily, string FontSize);

/// <summary>
/// Resolves the icons used across the Celbridge UI: file-type icons (by name or extension) and the
/// shared chrome icon set (by IconSymbol, by glyph name, or by raw glyph code).
/// </summary>
public interface IIconService
{
    /// <summary>
    /// Loads the definition data for all supported file-type icons.
    /// </summary>
    Result LoadDefinitions();

    /// <summary>
    /// Returns the file-type icon definition for the specified icon name.
    /// </summary>
    Result<FileIconDefinition> GetFileIcon(string iconName);

    /// <summary>
    /// Returns the file-type icon definition for the specified file extension.
    /// </summary>
    Result<FileIconDefinition> GetFileIconForExtension(string fileExtension);

    /// <summary>
    /// The default icon definition for file resources.
    /// </summary>
    FileIconDefinition DefaultFileIcon { get; }

    /// <summary>
    /// The default icon definition for folder resources.
    /// </summary>
    FileIconDefinition DefaultFolderIcon { get; }

    /// <summary>
    /// The ms-appx URI of the bundled chrome icon font, including the family suffix, for building a FontFamily.
    /// </summary>
    string IconFontFamilyUri { get; }

    /// <summary>
    /// Returns the glyph string for a known IconSymbol.
    /// </summary>
    string GetGlyph(IconSymbol icon);

    /// <summary>
    /// Returns the glyph string for a glyph name (e.g. "folder-fill"), or a fallback glyph if the name is unknown.
    /// </summary>
    string GetGlyph(string glyphName);

    /// <summary>
    /// Looks up the glyph string for a glyph name, returning false if the name is not defined.
    /// </summary>
    bool TryGetGlyph(string glyphName, out string glyph);
}
