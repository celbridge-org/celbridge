namespace Celbridge.UserInterface.Services;

/// <summary>
/// A per-landmark reveal seam owned by the component that hosts the landmark. It runs any
/// preparation a spotlight on this landmark needs beyond the default region reveal - for example
/// fading in a hover-only toolbar - and undoes the transient part when the spotlight clears.
/// </summary>
public interface ISpotlightLandmark
{
    /// <summary>
    /// Prepares the landmark to be spotlighted, revealing whatever its control needs to become
    /// visible and resolvable. Returns a failure when the landmark cannot be revealed.
    /// </summary>
    Task<Result> PreSpotlightAsync();

    /// <summary>
    /// Undoes any transient preparation once the spotlight moves away or clears. A sticky reveal
    /// does nothing here.
    /// </summary>
    void PostSpotlight();
}
