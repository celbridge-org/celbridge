using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// Owns the workspace's utilities: their lifecycle (created at project load, torn down at unload), their save
/// tick, and the dock orchestration that moves each utility's single WebView between the Utility Panel and a
/// document tab.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Creates each utility as a persistent workspace surface and returns the rail tabs describing
    /// them. Each utility is owned by this service until the workspace unloads. Utilities are given in
    /// declaration order, which is the rail order. A lazy-load utility is bound but its WebView is
    /// deferred to the first show.
    /// </summary>
    Task<IReadOnlyList<CustomUtility>> CreateUtilitiesAsync(IReadOnlyList<ResolvedEditor> resolvedEditors);

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
    /// Docks a utility at the given location, reparenting its single persistent WebView to that location's
    /// container (the Utility Panel rail or a document tab in the active document's section). Reveals or
    /// activates the utility at the destination. A no-op when it is already there.
    /// </summary>
    Task<Result> DockUtilityAsync(EditorId utilityId, DockLocation location);

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
