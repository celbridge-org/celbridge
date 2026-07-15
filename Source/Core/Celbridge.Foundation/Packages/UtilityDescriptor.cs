namespace Celbridge.Packages;

/// <summary>
/// Describes a utility: a WebView editor backed by one fixed resource under the utils: root rather than a
/// user-authored file. Parsed from the [utility] section of a document manifest; its presence marks the
/// contribution as a utility.
/// </summary>
public record UtilityDescriptor
{
    /// <summary>
    /// The fixed backing resource that holds the utility's persistent state (e.g. "utils:settings._emoji").
    /// The editor extension is derived from this value, so it is the single source of truth for identity.
    /// </summary>
    public string Resource { get; init; } = string.Empty;

    /// <summary>
    /// Package-relative path to the template that seeds the backing file when it is absent
    /// (e.g. "templates/default._emoji"). May be empty, in which case an empty file is seeded.
    /// </summary>
    public string Template { get; init; } = string.Empty;

    /// <summary>
    /// Icon glyph name for the rail button and the docked tab icon (e.g. "emoji-smile").
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Localization key for the rail button tooltip and accessible name. Also drives the docked tab title.
    /// </summary>
    public string Tooltip { get; init; } = string.Empty;
}
