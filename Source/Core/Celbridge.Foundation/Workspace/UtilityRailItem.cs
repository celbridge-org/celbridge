using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// The kinds of entry the Utility Panel rail presents. An item's kind fixes what it carries and what its
/// button does, so the payload follows from the kind rather than being set alongside it.
/// </summary>
public enum RailItemKind
{
    /// <summary>
    /// A utility that lives in the Utility Panel and cannot become a document. Explorer and Search are
    /// these, and so is a contribution that declares no dock area.
    /// </summary>
    PanelUtility,

    /// <summary>
    /// A WebView editor over its own state file that lives in the Utility Panel and can be docked into a
    /// document tab. Closing that tab returns it to the panel rather than destroying it.
    /// </summary>
    DockableUtility,

    /// <summary>
    /// A button that opens a document. It never occupies the panel, and the document it opens closes like
    /// any other.
    /// </summary>
    DocumentShortcut
}

/// <summary>
/// The view a rail item shows while it occupies the Utility Panel. Content is the view itself, FocusPanel
/// gives it keyboard focus, and FocusIdentity is the panel it reports focus as. PreservePanelFocus keeps the
/// reported panel while the platform bounces focus up onto the host, which a view that rebuilds its focused
/// element needs.
/// </summary>
public sealed record UtilityRailPanelView(
    object Content,
    Action FocusPanel,
    FocusPanelId FocusIdentity,
    bool PreservePanelFocus = false);

/// <summary>
/// One button on the Utility Panel rail and what it shows. Build one through the factory for its kind, which
/// is what keeps the payload and the kind in agreement.
/// </summary>
public sealed record UtilityRailItem
{
    /// <summary>
    /// Which kind of entry this is. Every other member is constrained by it.
    /// </summary>
    public required RailItemKind Kind { get; init; }

    /// <summary>
    /// The id that addresses this item.
    /// </summary>
    public required EditorId ItemId { get; init; }

    /// <summary>
    /// Automation id of the rail button, which is also its Spotlight landmark id.
    /// </summary>
    public required string LandmarkId { get; init; }

    /// <summary>
    /// Prefixed icon name for the rail button, and for the document tab when the item is a document.
    /// </summary>
    public required string IconName { get; init; }

    /// <summary>
    /// The item's human-readable, localized name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The tooltip shown on the rail button.
    /// </summary>
    public required string Tooltip { get; init; }

    /// <summary>
    /// The document area this item targets: where a dockable utility docks to, and where a document
    /// shortcut opens its document. Null for a panel utility, which never becomes a document. Where the item
    /// sits after it opens is the user's to change, like any other tab.
    /// </summary>
    public WorkspaceArea? DockArea { get; init; }

    /// <summary>
    /// The file this item presents, or empty for a rail item with no file behind it, which is what Explorer
    /// and Search are.
    /// </summary>
    public ResourceKey FileResource { get; init; } = ResourceKey.Empty;

    /// <summary>
    /// The editor that presents FileResource, or empty when there is no file. Named rather than resolved
    /// from the file, because a utils: file has no sidecar and no editor claims its extension.
    /// </summary>
    public EditorId EditorId { get; init; } = EditorId.Empty;

    /// <summary>
    /// The view this item shows in the Utility Panel. Null only for a document shortcut.
    /// </summary>
    public UtilityRailPanelView? PanelView { get; init; }

    /// <summary>
    /// A utility that lives in the Utility Panel and cannot be docked into a document tab. The file is empty
    /// for a built-in view with nothing behind it.
    /// </summary>
    public static UtilityRailItem CreatePanelUtility(
        EditorId itemId,
        string landmarkId,
        string iconName,
        string displayName,
        string tooltip,
        UtilityRailPanelView panelView,
        ResourceKey fileResource = default,
        EditorId editorId = default)
    {
        return new UtilityRailItem
        {
            Kind = RailItemKind.PanelUtility,
            ItemId = itemId,
            LandmarkId = landmarkId,
            IconName = iconName,
            DisplayName = displayName,
            Tooltip = tooltip,
            FileResource = fileResource,
            EditorId = editorId,
            PanelView = panelView
        };
    }

    /// <summary>
    /// A utility that lives in the Utility Panel and can be docked into a tab in the given document area.
    /// </summary>
    public static UtilityRailItem CreateDockableUtility(
        EditorId itemId,
        string landmarkId,
        string iconName,
        string displayName,
        string tooltip,
        ResourceKey fileResource,
        EditorId editorId,
        UtilityRailPanelView panelView,
        WorkspaceArea dockArea)
    {
        return new UtilityRailItem
        {
            Kind = RailItemKind.DockableUtility,
            ItemId = itemId,
            LandmarkId = landmarkId,
            IconName = iconName,
            DisplayName = displayName,
            Tooltip = tooltip,
            DockArea = dockArea,
            FileResource = fileResource,
            EditorId = editorId,
            PanelView = panelView
        };
    }

    /// <summary>
    /// A utility a package contributes, whose declared dock area decides its kind: an area makes it
    /// dockable into a tab there, and null keeps it in the Utility Panel.
    /// </summary>
    public static UtilityRailItem CreateContributedUtility(
        EditorId itemId,
        string landmarkId,
        string iconName,
        string displayName,
        string tooltip,
        ResourceKey fileResource,
        EditorId editorId,
        UtilityRailPanelView panelView,
        WorkspaceArea? dockArea)
    {
        if (dockArea is null)
        {
            return CreatePanelUtility(
                itemId, landmarkId, iconName, displayName, tooltip, panelView, fileResource, editorId);
        }

        return CreateDockableUtility(
            itemId, landmarkId, iconName, displayName, tooltip, fileResource, editorId, panelView, dockArea.Value);
    }

    /// <summary>
    /// A button that opens a document in the given area.
    /// </summary>
    public static UtilityRailItem CreateDocumentShortcut(
        EditorId itemId,
        string landmarkId,
        string iconName,
        string displayName,
        string tooltip,
        ResourceKey fileResource,
        EditorId editorId,
        WorkspaceArea dockArea)
    {
        return new UtilityRailItem
        {
            Kind = RailItemKind.DocumentShortcut,
            ItemId = itemId,
            LandmarkId = landmarkId,
            IconName = iconName,
            DisplayName = displayName,
            Tooltip = tooltip,
            DockArea = dockArea,
            FileResource = fileResource,
            EditorId = editorId
        };
    }
}
