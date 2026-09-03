namespace Celbridge.Packages;

/// <summary>
/// A file type declared by an editor contribution.
/// </summary>
public record EditorFileType
{
    /// <summary>
    /// The file extension this editor handles (e.g., ".note").
    /// </summary>
    public string FileExtension { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key shown in the Add File dialog.
    /// When omitted, falls back to the extension name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Prefixed icon name shown for files of this type, in place of the default file icon. Empty when
    /// the manifest declares none.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Hex colour applied to the declared icon. Empty when the manifest declares none, in which case the
    /// icon takes the theme's default colour.
    /// </summary>
    public string IconColor { get; init; } = string.Empty;

    /// <summary>
    /// Scale applied to the declared icon relative to the host's size. 1.0 draws it at the host's size; a
    /// larger value enlarges a glyph a font draws small within its em box.
    /// </summary>
    public double IconScale { get; init; } = 1.0;
}
