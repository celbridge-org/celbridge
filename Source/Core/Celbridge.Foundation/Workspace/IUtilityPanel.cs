using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.ProjectSettings;
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

    /// <summary>
    /// The Project Settings panel's utility id.
    /// </summary>
    public static readonly EditorId ProjectSettings = EditorId.Create("celbridge", "project-settings");
}

/// <summary>
/// A custom utility surface hosted as a rail item in the Utility Panel. Content is the utility's panel
/// view (a UIElement) and FocusPanel gives that view keyboard focus.
/// </summary>
public sealed record CustomUtility(
    EditorId UtilityId,
    string IconGlyphName,
    string Tooltip,
    string DisplayName,
    object Content,
    Action FocusPanel);

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
    /// Gets the Project Settings Panel for editing the .celbridge project config.
    /// </summary>
    IProjectSettingsPanel ProjectSettingsPanel { get; }

    /// <summary>
    /// The utility id of the surface currently active in the rail. Empty when no rail surface is active.
    /// </summary>
    EditorId ActiveUtilityId { get; }

    /// <summary>
    /// Reveals a utility wherever it currently lives: activates its document tab when it is docked as a document,
    /// otherwise selects its rail surface in the Utility Panel. A no-op when no utility has that id.
    /// </summary>
    void ShowUtility(EditorId utilityId);

    /// <summary>
    /// Appends custom utility rail items and their content hosts after the built-in items. Replaces any
    /// previously built items. Called on project load once the utility panels have been created.
    /// </summary>
    void BuildCustomUtilities(IReadOnlyList<CustomUtility> utilities);

    /// <summary>
    /// Removes all custom utility rail items and their content hosts. Called on project unload. Reverts
    /// the selection to Explorer if a custom utility was showing.
    /// </summary>
    void ClearCustomUtilities();

    /// <summary>
    /// Updates a custom utility's rail button to reflect its current dock location. When docked as a
    /// Document the button shows a docked cue and its click activates documentResource's tab. When in the
    /// UtilityPanel the button returns to normal, its click shows the panel surface, and documentResource
    /// is ignored.
    /// </summary>
    void SetUtilityDockLocation(EditorId utilityId, DockLocation location, ResourceKey documentResource);

    /// <summary>
    /// Briefly flashes a utility's rail button to draw attention to it. A no-op when no utility has that id.
    /// </summary>
    void FlashUtility(EditorId utilityId);

    /// <summary>
    /// Restores the previously active rail surface from workspace settings, falling back to Explorer when the
    /// persisted id no longer resolves. Called on project load after the utility items have been built.
    /// </summary>
    void RestoreSelectedUtility();
}
