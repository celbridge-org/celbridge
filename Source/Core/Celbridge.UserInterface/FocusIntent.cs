namespace Celbridge.UserInterface;

/// <summary>
/// Marks focus changes the application makes for its own housekeeping, such as re-focusing a list item after
/// the tree it sits in has been rebuilt. Uno reports programmatic focus back as Pointer focus, so FocusState
/// cannot tell those apart from the user moving focus; call sites say so explicitly by focusing through here.
/// </summary>
public static class FocusIntent
{
    private static int _restorationDepth;

    /// <summary>
    /// True while RestoreFocus is applying a focus change. The focus tracker skips reporting while this
    /// holds, leaving the focused panel unchanged.
    /// </summary>
    public static bool IsRestorationInProgress => _restorationDepth > 0;

    /// <summary>
    /// Focuses the control as a restoration rather than a deliberate move, so the focus tracker does not
    /// reclassify the focused panel. Returns false when the platform refused the focus change.
    /// </summary>
    public static bool RestoreFocus(Control control)
    {
        // Depth-counted rather than a flag: applying focus raises focus events synchronously, and a
        // handler may restore focus again before the outer call unwinds.
        _restorationDepth++;
        try
        {
            return control.Focus(FocusState.Programmatic);
        }
        finally
        {
            _restorationDepth--;
        }
    }
}
