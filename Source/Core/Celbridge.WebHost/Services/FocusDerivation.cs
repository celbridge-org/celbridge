namespace Celbridge.WebHost;

/// <summary>
/// The focus state the reconciler should establish. FocusWebSurface makes the focused web surface the
/// native focus target; YieldManagedFocus moves managed focus onto the placeholder so no managed control
/// claims keys destined for the page.
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
    public static DesiredFocus Derive(bool webSurfaceHoldsFocus, bool popupHoldsFocus)
    {
        // An open popup owns the keyboard, whatever the surface underneath it is. A popup reports no
        // panel, so the model still names that surface; yielding managed focus to the placeholder would
        // pull it out of the popup, and the popup would stop receiving input while still on screen.
        if (popupHoldsFocus)
        {
            return new DesiredFocus(
                FocusWebSurface: false,
                YieldManagedFocus: false);
        }

        // Otherwise two rules cover every case. A web surface holding focus becomes the native focus target
        // and the managed world yields the keyboard to it. Otherwise native focus returns to the host window
        // and managed focus stays wherever the managed world put it.
        return new DesiredFocus(
            FocusWebSurface: webSurfaceHoldsFocus,
            YieldManagedFocus: webSurfaceHoldsFocus);
    }
}
