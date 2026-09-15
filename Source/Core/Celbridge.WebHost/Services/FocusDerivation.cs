using Celbridge.UserInterface;

namespace Celbridge.WebHost;

/// <summary>
/// The focus state the reconciler should establish. FocusWebSurface makes the focused web surface the
/// native focus target. YieldManagedFocus moves managed focus onto the placeholder, so that the keys the
/// platform routes through the managed tree reach no control.
/// </summary>
public sealed record DesiredFocus(bool FocusWebSurface, bool YieldManagedFocus);

/// <summary>
/// Derives the desired focus state from the focus model, as a pure function so the mapping is testable.
/// </summary>
public static class FocusDerivation
{
    /// <summary>
    /// Maps the focus model to the focus state the reconciler should apply.
    /// </summary>
    public static DesiredFocus Derive(bool webSurfaceHoldsFocus, FocusLocation managedFocusLocation)
    {
        // An open popup owns the keyboard, whatever the surface underneath it is. A popup reports no
        // panel, so the model still names that surface. Yielding managed focus to the placeholder would
        // pull it out of the popup, and the popup would stop receiving input while still on screen.
        if (managedFocusLocation == FocusLocation.Popup)
        {
            return new DesiredFocus(
                FocusWebSurface: false,
                YieldManagedFocus: false);
        }

        // Otherwise two rules cover every case. A web surface holding focus becomes the native focus target
        // and the managed world yields the keyboard to it. Otherwise native focus returns to the host window
        // and managed focus stays wherever the managed world put it, unless that is an element no longer in
        // the tree, which holds the keyboard away from everything the user can see.
        return new DesiredFocus(
            FocusWebSurface: webSurfaceHoldsFocus,
            YieldManagedFocus: webSurfaceHoldsFocus || managedFocusLocation == FocusLocation.Detached);
    }
}
