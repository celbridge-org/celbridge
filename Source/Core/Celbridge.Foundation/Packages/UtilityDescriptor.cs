using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// Describes a workspace item the Utility Panel rail presents: a WebView editor backed by its own state file
/// under the utils: root rather than a user-authored file. Parsed from the [utility] section of an editor
/// manifest. AllowedAreas decides the item's scope, so one declaration covers both kinds.
/// </summary>
public record UtilityDescriptor
{
    /// <summary>
    /// The areas a utility occupies when its manifest declares none, which reproduces the placement every
    /// utility had before areas could be declared.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceArea> DefaultAllowedAreas =
    [
        WorkspaceArea.Utility,
        WorkspaceArea.Main
    ];

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
    /// The areas this utility is allowed to occupy, from the manifest's areas key. Never empty and never
    /// holds a duplicate.
    /// </summary>
    public IReadOnlyList<WorkspaceArea> AllowedAreas { get; init; } = DefaultAllowedAreas;

    /// <summary>
    /// The area the utility falls back to when no other one is named, from the manifest's default-area key.
    /// Always a member of AllowedAreas.
    /// </summary>
    public WorkspaceArea DefaultArea { get; init; } = WorkspaceArea.Utility;

    /// <summary>
    /// Whether this declares a workspace-scoped utility, which lives from project load to unload and parks
    /// its live view in the Utility Panel. False declares an open-scoped workspace item, created when its
    /// rail button opens it and destroyed when its tab closes.
    /// </summary>
    public bool IsWorkspaceScoped => AllowedAreas.Contains(WorkspaceArea.Utility);
}
