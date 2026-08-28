using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Ids for the built-in Utility Panel utilities, in the same "{scope}.{name}" form as custom utility ids.
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
/// Ids for the built-in rail launchers, in the same "{scope}.{name}" form as custom utility ids. A launcher
/// opens a document and never occupies the panel, so it is not a utility and takes no entry above; it still
/// needs a rail identity to be addressed by.
/// </summary>
public static class BuiltInLauncherIds
{
    /// <summary>
    /// The Project Settings launcher's rail id.
    /// </summary>
    public static readonly EditorId ProjectSettings = EditorId.Create("celbridge", "project-settings");

    /// <summary>
    /// The Community Workshop launcher's rail id.
    /// </summary>
    public static readonly EditorId Workshop = EditorId.Create("celbridge", "workshop");
}

/// <summary>
/// Interface for the Utility Panel, which hosts the built-in Explorer and Search plus any custom utilities.
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
    /// The utility id of the utility currently active in the rail. Empty when none is active.
    /// </summary>
    EditorId ActiveUtilityId { get; }

    /// <summary>
    /// Whether the rail carries a button with this id, whatever the item's scope. False for a utility that
    /// was declared but skipped at load, because no button was built for it.
    /// </summary>
    bool HasRailItem(EditorId itemId);

    /// <summary>
    /// Reveals a utility wherever it currently lives: activates its document tab when it is docked as a document,
    /// otherwise selects its rail item in the Utility Panel, presenting the panel when it is collapsed. A
    /// no-op when no utility has that id.
    /// </summary>
    void ShowUtility(EditorId utilityId);

    /// <summary>
    /// The built-in utility items this panel builds for itself. Their descriptors wrap live views, so the
    /// panel constructs them and the utility service records them into the rail register.
    /// </summary>
    IReadOnlyList<UtilityRailItem> GetBuiltInUtilityItems();

    /// <summary>
    /// Renders the rail register: builds a button, and where the item has a panel view a content host, for
    /// every item the panel does not already carry. Replaces any previously built items. Called on project
    /// load once the utilities have been created.
    /// </summary>
    void BuildRailItems(IReadOnlyList<UtilityRailItem> railItems);

    /// <summary>
    /// Removes every rail item the panel built from the register, along with their content hosts. Called on
    /// project unload. Reverts the selection to Explorer if a contributed utility was showing.
    /// </summary>
    void ClearRailItems();

    /// <summary>
    /// Tells the panel where a custom utility now lives, so the rail and the panel's content follow it.
    /// documentResource is the document hosting the utility while the area is a document area, and is
    /// otherwise ignored.
    /// </summary>
    void SetUtilityArea(EditorId utilityId, WorkspaceArea area, ResourceKey documentResource);

    /// <summary>
    /// Restores the previously active rail item from workspace settings, falling back to Explorer when the
    /// persisted id no longer resolves. Called on project load after the utility items have been built.
    /// </summary>
    void RestoreSelectedUtility();
}
