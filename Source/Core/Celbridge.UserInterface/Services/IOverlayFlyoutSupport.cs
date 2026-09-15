namespace Celbridge.UserInterface.Services;

/// <summary>
/// Keeps a flyout and the hosted web views underneath it out of each other's way. Every flyout that can
/// open over a document or the console needs wiring here.
/// </summary>
public interface IOverlayFlyoutSupport
{
    /// <summary>
    /// Wires the flyout so hosted web views ignore the mouse for as long as it is open, and the keyboard
    /// returns to the focused surface once it closes. Call once for each flyout, when the control that owns
    /// it is constructed. Suppressions nest, so a flyout opened over another keeps the suppression until
    /// both have closed.
    /// </summary>
    void Apply(FlyoutBase flyout);
}
