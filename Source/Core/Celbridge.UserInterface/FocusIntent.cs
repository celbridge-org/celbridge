using Celbridge.Workspace;

namespace Celbridge.UserInterface;

/// <summary>
/// Declares the intent of application-driven focus changes to the central focus tracker. Uno reports
/// programmatic focus back as Pointer focus, so FocusState cannot distinguish the application restoring
/// focus from the user moving it; restoration call sites declare the intent explicitly here instead.
/// </summary>
public static class FocusIntent
{
    private static int _restorationDepth;
    private static FocusPanelId _heldPanel = FocusPanelId.None;

    /// <summary>
    /// The panel that has just been given the keyboard and is being protected from stray focus events, or
    /// None when nothing is being protected. The focus tracker reports focus landing on this panel as usual,
    /// and moves the keyboard back when anything else takes it.
    /// </summary>
    public static FocusPanelId HeldPanel => _heldPanel;

    /// <summary>
    /// Protects the given panel until the next thing the user does. A click keeps producing focus events for
    /// a few milliseconds after the work it triggered has finished, so double-clicking a file in the Explorer
    /// can put the keyboard back on the tree just after the document has opened and taken it, leaving the
    /// user typing into the tree while the document looks focused. Nothing tells us a click has finished
    /// producing focus events, so the protection lasts until the user's next click or key press.
    ///
    /// The panel is passed in rather than read back from the focus service, because the document's own focus
    /// can arrive after this call: a document built from ordinary controls reports its focus a step later, so
    /// the focus service does not name it yet.
    /// </summary>
    public static void HoldPanelUntilNextInput(FocusPanelId panel)
    {
        _heldPanel = panel;
    }

    /// <summary>
    /// Ends the hold, so the next focus change reports its panel again. Called from the window's input
    /// handlers: any real user input means what follows is no longer the previous gesture's tail.
    /// </summary>
    public static void EndPanelHold()
    {
        _heldPanel = FocusPanelId.None;
    }

    /// <summary>
    /// Drops the state describing the current interaction so none of it carries into the next workspace.
    /// The hold is cleared by ordinary use, but it waits on the next user input, which a workspace teardown
    /// can pre-empt. The restoration depth is deliberately left alone, because RestoreFocus balances it in a
    /// finally and zeroing it mid-call would leave the counter negative and the guard off for good.
    /// </summary>
    public static void Reset()
    {
        EndPanelHold();
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
