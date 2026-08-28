using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Ids for the built-in Utility Panel surfaces, in the same "{scope}.{name}" form as custom utility ids.
/// </summary>
public static class BuiltInUtilityIds
{
    /// <summary>
    /// The Explorer panel's utility id.
    /// </summary>
    public static readonly EditorId Explorer = EditorId.Create("celbridge", "explorer");

    /// <summary>
    /// The Search panel's utility id.
    /// </summary>
    public static readonly EditorId Search = EditorId.Create("celbridge", "search");
}

/// <summary>
/// Interface for the Utility Panel, which hosts the Explorer and Search surfaces plus any custom utilities.
/// </summary>
public interface IUtilityPanel
{
    /// <summary>
    /// Gets the Explorer Panel for browsing project resources.
    /// </summary>
    IExplorerPanel ExplorerPanel { get; }

    /// <summary>
    /// Gets the Search Panel for searching within the project.
    /// </summary>
    ISearchPanel SearchPanel { get; }

    /// <summary>
    /// The utility id of the surface currently active in the rail. Empty when no rail surface is active.
    /// </summary>
    EditorId ActiveUtilityId { get; }

    /// <summary>
    /// Reveals a utility wherever it currently lives: activates its document tab when it is docked as a document,
    /// otherwise selects its rail surface in the Utility Panel, presenting the panel when it is collapsed. A
    /// no-op when no utility has that id.
    /// </summary>
    void ShowUtility(EditorId utilityId);

    /// <summary>
    /// Appends the contributed rail items and their content hosts between the built-in surfaces and the
    /// launchers. Replaces any previously built items. Called on project load once the utility panels have
    /// been created.
    /// </summary>
    void BuildRailItems(IReadOnlyList<UtilityRailItem> railItems);

    /// <summary>
    /// Removes all contributed rail items and their content hosts. Called on project unload. Reverts the
    /// selection to Explorer if a contributed utility was showing.
    /// </summary>
    void ClearRailItems();

    /// <summary>
    /// Tells the panel where a custom utility now lives, so the rail and the panel's surface follow it.
    /// documentResource is the document hosting the utility while the area is a document area, and is
    /// otherwise ignored.
    /// </summary>
    void SetUtilityArea(EditorId utilityId, WorkspaceArea area, ResourceKey documentResource);

    /// <summary>
    /// Restores the previously active rail surface from workspace settings, falling back to Explorer when the
    /// persisted id no longer resolves. Called on project load after the utility items have been built.
    /// </summary>
    void RestoreSelectedUtility();
}
