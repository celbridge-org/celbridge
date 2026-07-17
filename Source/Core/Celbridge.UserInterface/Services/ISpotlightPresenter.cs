namespace Celbridge.UserInterface.Services;

/// <summary>
/// The presentation seam the spotlight service drives to show the spotlight, implemented by the
/// spotlight view control hosted in the app shell. The service never touches XAML directly. A
/// single presenter is active at a time.
/// </summary>
public interface ISpotlightPresenter
{
    /// <summary>
    /// Resolves a landmark id to a live element in the visual tree, or null when no matching
    /// realised control is currently present.
    /// </summary>
    FrameworkElement? ResolveLandmark(string landmarkId);

    /// <summary>
    /// Shows the spotlight pointing at the target element with the supplied label.
    /// </summary>
    void ShowSpotlight(FrameworkElement target, string label);

    /// <summary>
    /// Hides the spotlight.
    /// </summary>
    void HideSpotlight();

    /// <summary>
    /// Raised when the spotlight has closed for any reason (close button, light dismiss, or a
    /// programmatic hide), so the service can release the state that outlived the visible spotlight.
    /// </summary>
    event EventHandler? SpotlightClosed;
}
