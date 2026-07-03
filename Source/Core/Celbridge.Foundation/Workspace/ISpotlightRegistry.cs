namespace Celbridge.Workspace;

/// <summary>
/// A spotlightable UI landmark. Id matches the AutomationProperties.AutomationId set on the
/// control and is the resolution key; Region is the layout region to reveal before showing it,
/// or null when the landmark does not gate on a region.
/// </summary>
public partial record class LandmarkDescriptor(string Id, LayoutRegion? Region);

/// <summary>
/// The runtime vocabulary of spotlightable UI landmarks. Built-in landmarks are registered at
/// startup and packages can register their own; app_spotlight validates and enumerates targets
/// against this registry. It lives in Foundation so the agent tools can reach it.
/// </summary>
public interface ISpotlightRegistry
{
    /// <summary>
    /// Registers a landmark, replacing any existing entry with the same id.
    /// </summary>
    void RegisterLandmark(LandmarkDescriptor landmark);

    /// <summary>
    /// Unregisters the landmark with the given id.
    /// </summary>
    void UnregisterLandmark(string landmarkId);

    /// <summary>
    /// Returns all registered landmarks.
    /// </summary>
    IReadOnlyList<LandmarkDescriptor> GetLandmarks();

    /// <summary>
    /// Looks up a landmark by id. Returns false when no landmark with that id is registered.
    /// </summary>
    bool TryGetLandmark(string landmarkId, out LandmarkDescriptor? landmark);
}
