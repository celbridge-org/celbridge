namespace Celbridge.Packages;

/// <summary>
/// Describes a utility editor: a WebView editor whose instances are workspace fixtures, each backed by
/// its own state file under the utils: root rather than a user-authored file. Parsed from the [utility]
/// section of an editor manifest.
/// </summary>
public record UtilityDescriptor
{
    /// <summary>
    /// File extension of an instance's backing state file (e.g. "._notepad"). The host derives the
    /// full path from the instance id, as "utils:{instanceId}{ResourceExtension}".
    /// </summary>
    public string ResourceExtension { get; init; } = string.Empty;

    /// <summary>
    /// Package-relative path to the template that seeds the backing file when it is absent
    /// (e.g. "templates/default._notepad"). May be empty, in which case an empty file is seeded.
    /// </summary>
    public string Template { get; init; } = string.Empty;

    /// <summary>
    /// Icon glyph name for the rail button and the docked tab icon (e.g. "sticky").
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Localization key for the rail button tooltip and accessible name. Also drives the docked tab title.
    /// </summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>
    /// When true, view creation is deferred to the first show of the utility. Declared once by the
    /// editor and applies to every instance of it.
    /// </summary>
    public bool LazyLoad { get; init; }
}
