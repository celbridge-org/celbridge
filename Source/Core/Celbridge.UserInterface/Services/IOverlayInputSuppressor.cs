namespace Celbridge.UserInterface.Services;

/// <summary>
/// Keeps input meant for a managed overlay from also reaching hosted web views underneath it. Every flyout
/// that can open over a document or the console needs wiring here: a hosted web view is a native view above
/// the canvas the overlay is drawn on, so it takes the click as well as the overlay.
/// </summary>
public interface IOverlayInputSuppressor
{
    /// <summary>
    /// Stops hosted web views taking mouse input for as long as the flyout is open. Call once for each
    /// flyout, when the control that owns it is constructed. Suppressions nest, so a flyout opened over
    /// another keeps the suppression until both have closed. Does nothing on platforms where web views
    /// already take part in the normal input routing.
    /// </summary>
    void SuppressWhileOpen(FlyoutBase flyout);
}
