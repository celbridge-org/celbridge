namespace Celbridge.WebHost;

/// <summary>
/// The focus state the reconciler should establish. FocusWebSurface makes the focused web surface the
/// native focus target; ParkManagedFocus parks managed focus on the sink so no managed control claims
/// keys destined for the page.
/// </summary>
public sealed record DesiredFocus(bool FocusWebSurface, bool ParkManagedFocus);

/// <summary>
/// Derives the desired focus state from the focus model, as a pure function so the mapping is testable.
/// </summary>
public static class FocusDerivation
{
    /// <summary>
    /// Maps whether a hosted web surface holds focus to the focus state the reconciler should apply.
    /// </summary>
    public static DesiredFocus Derive(bool webSurfaceHoldsFocus)
    {
        // Two rules cover every case. A web surface holding focus becomes the native focus target with
        // managed focus parked. Otherwise native focus returns to the host window and managed focus
        // stays wherever the managed world put it.
        return new DesiredFocus(
            FocusWebSurface: webSurfaceHoldsFocus,
            ParkManagedFocus: webSurfaceHoldsFocus);
    }
}
