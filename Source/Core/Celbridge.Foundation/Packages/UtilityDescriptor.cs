namespace Celbridge.Packages;

/// <summary>
/// Describes a utility editor: a WebView editor that is a workspace fixture, backed by its own state
/// file under the utils: root rather than a user-authored file. Parsed from the [utility] section of an
/// editor manifest.
/// </summary>
public record UtilityDescriptor
{
    /// <summary>
    /// File extension of the backing state file (e.g. "._utildemo"). The host derives the full path
    /// from the editor id, as "utils:{editorId}{ResourceExtension}".
    /// </summary>
    public string ResourceExtension { get; init; } = string.Empty;

    /// <summary>
    /// Package-relative path to the template that seeds the backing file when it is absent
    /// (e.g. "templates/default._utildemo"). May be empty, in which case an empty file is seeded.
    /// </summary>
    public string Template { get; init; } = string.Empty;

    /// <summary>
    /// Icon glyph name for the rail button and the docked tab icon (e.g. "sticky").
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// When true, view creation is deferred to the first show of the utility.
    /// </summary>
    public bool LazyLoad { get; init; }
}
