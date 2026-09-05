using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// Owns the workspace's utilities: their lifecycle (created at project load, torn down at unload), their save
/// tick, and the dock orchestration that moves each utility's single WebView between the Utility Panel and a
/// document tab. Also holds the register of every workspace item the Utility Panel rail presents, and the
/// area each one occupies.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Records the built-in utility items the Utility Panel builds for itself. Called once per workspace
    /// load.
    /// </summary>
    void RegisterBuiltInUtilityItems(IReadOnlyList<UtilityRailItem> builtInUtilityItems);

    /// <summary>
    /// Seeds each declared utility's backing file and records it in the rail register, along with the
    /// document shortcuts, in declaration order, which is the rail order. Each utility also gets a
    /// persistent view, owned by this service until the workspace unloads.
    /// </summary>
    Task CreateUtilitiesAsync(IReadOnlyList<ResolvedEditor> resolvedEditors);

    /// <summary>
    /// Every workspace item the rail presents, in rail order: the registered built-in utilities, then the
    /// contribution utilities, then the document shortcuts. Empty until the utilities have been created.
    /// </summary>
    IReadOnlyList<UtilityRailItem> GetRailItems();

    /// <summary>
    /// The area a rail item currently occupies, or null when nothing presents it: a document shortcut whose
    /// document is closed, or an id the register does not hold. A utility always occupies an area.
    /// </summary>
    WorkspaceArea? GetCurrentArea(EditorId itemId);

    /// <summary>
    /// Restores a utility that was docked as a document in the previous session into a document tab at the given
    /// address, reparenting its already-instantiated WebView out of the Utility Panel. Does not activate, flash,
    /// or change which item the panel shows. Fails if no utility owns the resource.
    /// </summary>
    Task<Result> RestoreDockedUtilityAsync(ResourceKey resource, DocumentAddress address);

    /// <summary>
    /// Returns true when a live utility with this id exists, meaning one that was created at workspace load and
    /// can be shown or docked. A declared utility that was skipped at load is not live: its backing resource,
    /// seed, or initialization failed.
    /// </summary>
    bool HasUtility(EditorId utilityId);

    /// <summary>
    /// Docks a utility in the given area, reparenting its single persistent WebView to that area's
    /// container (the Utility Panel rail or a document tab in the area's primary section). Reveals or
    /// activates the utility at the destination. A no-op when it is already there.
    /// </summary>
    Task<Result> DockUtilityAsync(EditorId utilityId, WorkspaceArea area);

    /// <summary>
    /// Returns the id of the utility currently docked as the given document resource, or null when the resource
    /// is not a docked utility.
    /// </summary>
    EditorId? GetDockedUtilityId(ResourceKey resource);

    /// <summary>
    /// The utilities, as items the workspace save tick can flush. A utility docked into a document tab is
    /// listed here too, because its panel view stays its owner wherever it is presented.
    /// </summary>
    IReadOnlyList<ISaveableWorkspaceItem> GetSaveableItems();

    /// <summary>
    /// Saves any pending changes in the utilities and releases them. Called on workspace unload.
    /// </summary>
    Task TeardownUtilitiesAsync();
}
