using Celbridge.Documents;
using Celbridge.Packages;

namespace Celbridge.Workspace;

/// <summary>
/// Owns the workspace's utilities: their lifecycle (created at project load, torn down at unload), their save
/// tick, and the dock orchestration that moves each utility's single WebView between the Utility Panel and a
/// document tab. Presentation lives in IUtilityPanel; this is the logic behind it.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Creates each utility as a persistent workspace surface and returns the rail tabs describing them. Each
    /// utility is owned by this service until the workspace unloads. Contributions are given in display order.
    /// </summary>
    Task<IReadOnlyList<ContributedUtility>> CreateUtilitiesAsync(IReadOnlyList<CustomDocumentEditorContribution> contributions);

    /// <summary>
    /// Restores a utility that was docked as a document in the previous session into a document tab at the given
    /// address, reparenting its already-instantiated WebView out of the Utility Panel. Unlike an interactive
    /// dock this does not activate, flash, or change the shown panel surface. Fails if no utility owns the
    /// resource.
    /// </summary>
    Result RestoreDockedUtility(ResourceKey resource, DocumentAddress address);

    /// <summary>
    /// Docks a utility at the given location, reparenting its single persistent WebView to that location's
    /// container (the Utility Panel rail or a document tab in the active document's section) and reusing the
    /// same instance. Reveals or activates the utility at the destination; a no-op when it is already there.
    /// </summary>
    Task<Result> DockUtilityAsync(UtilityId utilityId, DockLocation location);

    /// <summary>
    /// Returns the id of the utility currently docked as the given document resource, or null when the resource
    /// is not a docked utility. The close path uses this to dock a utility back into the panel rather than
    /// destroy its document tab.
    /// </summary>
    UtilityId? GetDockedUtilityId(ResourceKey resource);

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
