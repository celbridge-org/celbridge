using Celbridge.Explorer;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Ids for the built-in Utility Panel surfaces, in the same "{scope}.{name}" form as contributed utility ids,
/// so one id scheme addresses every utility (built-in and contributed).
/// </summary>
public static class BuiltInUtilityIds
{
    /// <summary>
    /// The Explorer panel's utility id.
    /// </summary>
    public static readonly UtilityId Explorer = UtilityId.Create("celbridge", "explorer");

    /// <summary>
    /// The Search panel's utility id.
    /// </summary>
    public static readonly UtilityId Search = UtilityId.Create("celbridge", "search");
}

/// <summary>
/// A contributed utility surface hosted as a rail item in the Utility Panel. Content is the utility's panel
/// view (a UIElement), typed as object so this Foundation contract does not depend on the UI framework.
/// FocusPanel gives the panel's content keyboard focus, called when its rail item is selected.
/// </summary>
public sealed record ContributedUtility(
    UtilityId UtilityId,
    string IconGlyphName,
    string Tooltip,
    string DisplayName,
    object Content,
    Action FocusPanel);

/// <summary>
/// Interface for the Utility Panel, which hosts the Explorer and Search surfaces plus any contributed utilities.
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
    /// The utility id of the surface currently active in the rail: a built-in id (BuiltInUtilityIds) for Explorer
    /// or Search, or a contributed utility id. Empty when no rail surface is active.
    /// </summary>
    UtilityId ActiveUtilityId { get; }

    /// <summary>
    /// Reveals a utility wherever it currently lives: activates its document tab when it is docked as a document,
    /// otherwise selects its rail surface in the Utility Panel. Works for built-in and contributed utilities. A
    /// no-op when no utility has that id.
    /// </summary>
    void ShowUtility(UtilityId utilityId);

    /// <summary>
    /// Appends contributed utility rail items and their content hosts after the built-in items. Replaces any
    /// previously built items. Called on project load once the utility panels have been created.
    /// </summary>
    void BuildContributedUtilities(IReadOnlyList<ContributedUtility> utilities);

    /// <summary>
    /// Removes all contributed utility rail items and their content hosts. Called on project unload. Reverts
    /// the selection to Explorer if a contributed utility was showing.
    /// </summary>
    void ClearContributedUtilities();

    /// <summary>
    /// Updates a contributed utility's rail button to reflect its current dock location. When docked as a
    /// Document the button shows a docked cue and its click activates documentResource's tab; when in the
    /// UtilityPanel the button returns to normal and its click shows the panel surface. documentResource is
    /// ignored for the UtilityPanel location.
    /// </summary>
    void SetUtilityDockLocation(UtilityId utilityId, DockLocation location, ResourceKey documentResource);

    /// <summary>
    /// Briefly flashes a utility's rail button to draw attention to it, e.g. when the utility is undocked from a
    /// document tab and its rail button becomes available again. A no-op when no utility has that id.
    /// </summary>
    void FlashUtility(UtilityId utilityId);

    /// <summary>
    /// Restores the previously active rail surface from workspace settings, falling back to Explorer when the
    /// persisted id no longer resolves. Enables persisting later user selections. Called on project load after
    /// the utility items have been built.
    /// </summary>
    void RestoreSelectedUtility();
}
