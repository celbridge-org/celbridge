namespace Celbridge.UserInterface;

/// <summary>
/// Declares the intent of application-driven focus changes to the central focus tracker. Uno reports
/// programmatic focus back as Pointer focus, so FocusState cannot distinguish the application restoring
/// focus from the user moving it; restoration call sites declare the intent explicitly here instead.
/// </summary>
public static class FocusIntent
{
    private static int _restorationDepth;
    private static bool _panelClaimSuppressed;

    /// <summary>
    /// True while a deliberate focus grant is holding the focused panel against focus events still being
    /// dispatched from the gesture that triggered it. The focus tracker reports no panel while this holds.
    /// </summary>
    public static bool IsPanelClaimSuppressed => _panelClaimSuppressed;

    /// <summary>
    /// Holds the focused panel until the next user input. A gesture's focus events can be dispatched after
    /// the work it triggered has completed, so a double-click that opens a document can finish claiming the
    /// tree it was issued from milliseconds after the document has taken focus, leaving keys going to the
    /// tree while the document looks focused. There is no event marking the end of a gesture's focus
    /// dispatch, so the hold ends at the next thing the user does.
    /// </summary>
    public static void SuppressPanelClaimsUntilNextInput()
    {
        _panelClaimSuppressed = true;
    }

    /// <summary>
    /// Ends the hold, so the next focus change reports its panel again. Called from the window's input
    /// handlers: any real user input means what follows is no longer the previous gesture's tail.
    /// </summary>
    public static void EndPanelClaimSuppression()
    {
        _panelClaimSuppressed = false;
    }

    /// <summary>
    /// Drops the state describing the current interaction so none of it carries into the next workspace.
    /// The hold is cleared by ordinary use, but it waits on the next user input, which a workspace teardown
    /// can pre-empt. The restoration depth is deliberately left alone, because RestoreFocus balances it in a
    /// finally and zeroing it mid-call would leave the counter negative and the guard off for good.
    /// </summary>
    public static void Reset()
    {
        EndPanelClaimSuppression();
    }

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
