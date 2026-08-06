namespace Celbridge.UserInterface;

/// <summary>
/// Declares the intent of application-driven focus changes to the central focus tracker. Uno reports
/// programmatic focus back as Pointer focus, so FocusState cannot distinguish the application restoring
/// focus from the user moving it; restoration call sites declare the intent explicitly here instead.
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
