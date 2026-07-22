namespace Celbridge.Packages;

/// <summary>
/// The host-owned group a file type is listed under on the Project Settings File Types page. The set is
/// closed; a file type whose manifest declares no category has a null category and is classified by the
/// host from its extension instead.
/// </summary>
public enum FileTypeCategory
{
    Text,
    Image,
    Audio,
    Video,
    Data,
    Document,

    /// <summary>
    /// A format specific to this app or one of its packages, rather than a standard file type. Assigned
    /// by provenance (a package editor claims it), not declared in a manifest.
    /// </summary>
    Custom,
}

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
    /// The category this file type is grouped under on the File Types page. Null when the manifest
    /// declares no category.
    /// </summary>
    public FileTypeCategory? Category { get; init; }

    /// <summary>
    /// Glyph name shown for files of this type in place of the bundled icon theme's choice. Empty when
    /// the manifest declares none.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Hex colour applied to the declared icon. Empty when the manifest declares none, in which case the
    /// icon takes the theme's default colour.
    /// </summary>
    public string IconColor { get; init; } = string.Empty;
}
