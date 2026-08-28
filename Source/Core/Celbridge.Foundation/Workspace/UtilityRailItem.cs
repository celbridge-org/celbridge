using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// The resource a rail item opens as a document, and the editor that presents it.
/// </summary>
public sealed record UtilityRailResource(
    ResourceKey Resource,
    EditorId Editor);

/// <summary>
/// The view a rail item shows while it occupies the Utility Panel. Content is the view itself, FocusPanel
/// gives it keyboard focus, and FocusIdentity is the panel it reports focus as. The application supplies this
/// for Explorer and Search; for a contribution utility it is the view already built from the item's resource.
/// PreservePanelFocus keeps the reported panel while the platform bounces focus up onto the host, which a
/// view that rebuilds its focused element needs.
/// </summary>
public sealed record UtilityRailPanelView(
    object Content,
    Action FocusPanel,
    FocusPanelId FocusIdentity,
    bool PreservePanelFocus = false);

/// <summary>
/// One button on the Utility Panel rail and what it shows. Resource is set when the item can be a document
/// and PanelView when it can occupy the Utility Panel, matching the areas it allows; at least one is always
/// set, and a dockable utility carries both.
/// </summary>
public sealed record UtilityRailItem
{
    // Where an item that names no areas may go. The Utility Panel alone, because it is the one area every
    // rail item can occupy and the one DefaultArea also falls back to. Every item the rail builds names its
    // own areas: a contribution utility from its manifest, and the built-in items from what they are.
    private static readonly IReadOnlyList<WorkspaceArea> PanelOnlyAreas =
    [
        WorkspaceArea.Utility
    ];

    /// <summary>
    /// The id that addresses this item.
    /// </summary>
    public EditorId ItemId { get; init; }

    /// <summary>
    /// Automation id of the rail button, which is also its Spotlight landmark id. Carried rather than
    /// derived from ItemId, because the built-in items are addressed by short names while a contribution's
    /// landmark is built from its full editor id.
    /// </summary>
    public string LandmarkId { get; init; } = string.Empty;

    /// <summary>
    /// Prefixed icon name for the rail button, and for the document tab when the item is a document.
    /// </summary>
    public string IconName { get; init; } = string.Empty;

    /// <summary>
    /// The item's human-readable, localized name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The tooltip shown on the rail button.
    /// </summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>
    /// The areas this item is allowed to occupy. Never empty and never holds a duplicate.
    /// </summary>
    public IReadOnlyList<WorkspaceArea> AllowedAreas { get; init; } = PanelOnlyAreas;

    /// <summary>
    /// The area the item falls back to when no other one is named: where a launcher opens its document, and
    /// where a utility is restored when its stored area is no longer allowed. Always a member of AllowedAreas.
    /// </summary>
    public WorkspaceArea DefaultArea { get; init; } = WorkspaceArea.Utility;

    /// <summary>
    /// The resource this item opens and the editor that presents it, or null when the item cannot be a
    /// document. Set whenever AllowedAreas holds a document area.
    /// </summary>
    public UtilityRailResource? Resource { get; init; }

    /// <summary>
    /// The view this item shows in the Utility Panel, or null when the item cannot occupy the panel. Set
    /// whenever AllowedAreas holds the utility area.
    /// </summary>
    public UtilityRailPanelView? PanelView { get; init; }
}
