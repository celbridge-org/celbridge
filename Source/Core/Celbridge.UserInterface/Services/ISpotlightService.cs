using Microsoft.UI.Xaml;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Owns the single spotlight callout: showing it on a resolved element, clearing it, the
/// optional auto-clear timer, click-to-dismiss, and the one-at-a-time invariant. The spotlight
/// presenter hosted in the app shell registers itself as the surface the service drives.
/// </summary>
public interface ISpotlightService
{
    /// <summary>
    /// Registers the presenter the service drives. A single presenter is active at a time, so a
    /// later registration replaces an earlier one.
    /// </summary>
    void RegisterPresenter(ISpotlightPresenter presenter);

    /// <summary>
    /// Unregisters the presenter. Clears the current spotlight and the slot only when the supplied
    /// presenter is still the registered one, so a stale unregister after a replacement is a no-op.
    /// </summary>
    void UnregisterPresenter(ISpotlightPresenter presenter);

    /// <summary>
    /// Registers a reveal provider for a landmark id. The provider runs extra preparation when the
    /// landmark is spotlighted, on top of the default region reveal, and undoes it on clear.
    /// </summary>
    void RegisterLandmark(string landmarkId, ISpotlightLandmark landmark);

    /// <summary>
    /// Unregisters the reveal provider for a landmark id.
    /// </summary>
    void UnregisterLandmark(string landmarkId);

    /// <summary>
    /// Reveals the named landmark (opening its region, running its reveal provider) and shows the
    /// spotlight on it with a label and an optional auto-clear delay in milliseconds (zero to
    /// persist). Returns a failure with a descriptive message when the landmark cannot be made
    /// visible. Replaces any active spotlight.
    /// </summary>
    Task<Result> ShowSpotlightAsync(string target, string label, int durationMs);

    /// <summary>
    /// Shows the spotlight on an already-resolved element with a label and an optional auto-clear
    /// delay in milliseconds (zero to persist). Replaces any active spotlight.
    /// </summary>
    void ShowSpotlight(FrameworkElement target, string label, int durationMs);

    /// <summary>
    /// Clears the current spotlight, if any.
    /// </summary>
    void ClearSpotlight();
}
