using Celbridge.Workspace;

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
    /// The document area the utility docks into, from the manifest's dock-area key. Null when it declares
    /// dock-area = "none", which keeps it in the Utility Panel and hides its "Open as document" control.
    /// </summary>
    public WorkspaceArea? DockArea { get; init; } = WorkspaceArea.Main;
}
