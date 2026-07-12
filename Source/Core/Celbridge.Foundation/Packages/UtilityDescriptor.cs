namespace Celbridge.Packages;

/// <summary>
/// Describes a utility document: a custom WebView editor that is not tied to a user-authored file
/// resource but instead owns one fixed backing resource under the utils: root and contributes a
/// title-bar launcher. Parsed from the [utility] section of a document manifest and carried on the
/// custom contribution. Its presence is what marks a contribution as a utility.
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
    /// Icon glyph name for the launcher button and the tab icon (e.g. "emoji-smile").
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Localization key for the launcher tooltip and accessible name. Also drives the tab title.
    /// </summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>
    /// When true, the utility is opened automatically when the project loads.
    /// </summary>
    public bool AutoOpen { get; init; }

    /// <summary>
    /// When false, the user cannot close the utility's tab. Teardown at project unload still closes it.
    /// </summary>
    public bool Closable { get; init; } = true;
}
