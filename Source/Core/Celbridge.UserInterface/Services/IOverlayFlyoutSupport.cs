namespace Celbridge.UserInterface.Services;

/// <summary>
/// Keeps a flyout and the hosted web views underneath it out of each other's way. Every flyout that can
/// open over a document or the console needs wiring here. A menu nested inside one, such as the submenu a
/// MenuFlyoutSubItem opens, is not a flyout and cannot be wired: dismissing one of those while its parent
/// stays open leaves the keyboard on the item it was showing.
/// </summary>
public interface IOverlayFlyoutSupport
{
    /// <summary>
    /// Wires the flyout so hosted web views ignore the mouse for as long as it is open, and the keyboard
    /// is given up once it closes rather than left on an item that is gone. Call once for each flyout, when
    /// the control that owns it is constructed. Suppressions nest, so a flyout opened over another keeps the
    /// suppression until both have closed. Both halves act only on the heads where hosted web views take
    /// native focus and native mouse input of their own.
    /// </summary>
    void Apply(FlyoutBase flyout);
}
