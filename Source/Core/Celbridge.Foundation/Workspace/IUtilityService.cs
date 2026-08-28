using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// Owns the workspace's utilities: their lifecycle (created at project load, torn down at unload), their save
/// tick, and the dock orchestration that moves each utility's single WebView between the Utility Panel and a
/// document tab. Also holds the register of every workspace item the Utility Panel rail presents, and the
/// area each one occupies, so a caller reads placement from the record rather than from the view showing it.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Records the built-in utility items the Utility Panel builds for itself. Their descriptors wrap live
    /// views, so the panel constructs them and publishes them here for the register to hold. Called once per
    /// workspace load, before the utilities are created.
    /// </summary>
    void RegisterBuiltInUtilityItems(IReadOnlyList<UtilityRailItem> builtInUtilityItems);

    /// <summary>
    /// Creates each utility as a persistent workspace surface and records it in the rail register, along with
    /// the launchers. Each utility is owned by this service until the workspace unloads. Utilities are created
    /// in declaration order, which is the rail order. A lazy-load utility is bound but its WebView is deferred
    /// to the first show.
    /// </summary>
    Task CreateUtilitiesAsync(IReadOnlyList<ResolvedEditor> resolvedEditors);

    /// <summary>
    /// Every workspace item the rail presents, in rail order: the registered built-in utilities, then the
    /// contribution utilities, then the launchers. Empty until the utilities have been created.
    /// </summary>
    IReadOnlyList<UtilityRailItem> GetRailItems();

    /// <summary>
    /// The area a rail item currently occupies, which is its descriptor's default area until it moves.
    /// Reports the Utility Panel for an id the register does not hold.
    /// </summary>
    WorkspaceArea GetItemArea(EditorId itemId);

    /// <summary>
    /// Initializes a lazy-load utility's WebView if it has not been created yet. A no-op for
    /// already-initialized utilities, built-in utilities, and unknown ids.
    /// </summary>
    Task<Result> EnsureUtilityInitializedAsync(EditorId utilityId);

    /// <summary>
    /// Restores a utility that was docked as a document in the previous session into a document tab at the given
    /// address, reparenting its already-instantiated WebView out of the Utility Panel. Does not activate, flash,
    /// or change the shown panel surface. Fails if no utility owns the resource.
    /// </summary>
    Task<Result> RestoreDockedUtility(ResourceKey resource, DocumentAddress address);

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
    /// Ticks each utility's save timer and flushes the ones that are due. Called on the workspace update loop.
    /// Per-utility save failures are logged, not propagated.
    /// </summary>
    Task SaveModifiedUtilities(double deltaTime);

    /// <summary>
    /// Saves any pending changes in the utilities and releases them. Called on workspace unload.
    /// </summary>
    Task TeardownUtilitiesAsync();
}
