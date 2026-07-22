namespace Celbridge.UserInterface;

/// <summary>
/// A glyph resolved from one of the bundled icon fonts. The font family is a resource dictionary key
/// rather than a font itself, so the icon service stays free of framework types.
/// </summary>
public record IconGlyph(string FontCharacter, string FontFamily);

/// <summary>
/// Everything required to draw an icon with the FontIcon control: the glyph, the font it belongs to,
/// and the colour and size it is drawn at.
/// </summary>
public record IconDefinition(string FontCharacter, string FontColor, string FontFamily, string FontSize);

/// <summary>
/// Resolves the icons used across the Celbridge UI. Icons are named, never addressed by codepoint, and
/// every name carries a prefix identifying the font it comes from (for example "bs-gear"). File-type
/// icons are additionally resolvable by file extension through the bundled icon theme.
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
    Result<IconDefinition> GetFileIcon(string iconName);

    /// <summary>
    /// Returns the file-type icon definition for the specified file extension. A registered override for
    /// the extension wins over the bundled icon theme.
    /// </summary>
    Result<IconDefinition> GetFileIconForExtension(string fileExtension);

    /// <summary>
    /// Builds an icon definition from a prefixed icon name and an optional hex colour, failing when the
    /// name is unknown or the colour is malformed.
    /// </summary>
    Result<IconDefinition> CreateIcon(string iconName, string colorHex);

    /// <summary>
    /// Replaces the per-extension icon overrides consulted ahead of the bundled theme. Each discovery
    /// pass supplies the full set, so overrides from a previous workspace do not linger.
    /// </summary>
    void SetFileIconOverrides(IReadOnlyDictionary<string, IconDefinition> overrides);

    /// <summary>
    /// The default icon definition for file resources.
    /// </summary>
    IconDefinition DefaultFileIcon { get; }

    /// <summary>
    /// The default icon definition for folder resources.
    /// </summary>
    IconDefinition DefaultFolderIcon { get; }

    /// <summary>
    /// Returns the glyph for a known IconSymbol.
    /// </summary>
    IconGlyph GetGlyph(IconSymbol icon);

    /// <summary>
    /// Returns the glyph for a prefixed icon name (for example "bs-folder-fill"), or a fallback glyph if
    /// the name is unknown.
    /// </summary>
    IconGlyph GetGlyph(string iconName);

    /// <summary>
    /// Looks up the glyph for a prefixed icon name, returning false if the name is not defined.
    /// </summary>
    bool TryGetGlyph(string iconName, out IconGlyph glyph);
}
