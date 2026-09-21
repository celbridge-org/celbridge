using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Where a caret motion lands. The platform names these as editing commands rather than as keys, so the
/// chord that produces one differs by platform.
/// </summary>
public enum CaretMotion
{
    LineStart,
    LineEnd,
    DocumentStart,
    DocumentEnd
}

/// <summary>
/// The managed keyboard focus of the window: where it currently rests, and the means to give it up.
/// </summary>
public interface IManagedFocus
{
    /// <summary>
    /// Where managed keyboard focus currently rests. Answered from the focused element each time it is
    /// asked, so it cannot go stale, and reads as the main content while nothing holds focus.
    /// </summary>
    FocusLocation FocusLocation { get; }

    /// <summary>
    /// The workspace panel the element holding managed keyboard focus belongs to. None while nothing holds
    /// focus, and while focus rests outside every panel, in an open popup, or on an element no longer in the
    /// window's tree.
    /// </summary>
    FocusPanelId Panel { get; }

    /// <summary>
    /// Whether a text editing control holds managed keyboard focus.
    /// </summary>
    bool IsTextControlFocused { get; }

    /// <summary>
    /// Whether the text editing control holding managed keyboard focus can perform the verb right now.
    /// False when no text control has focus.
    /// </summary>
    bool CanPerformTextEditing(EditIntent intent);

    /// <summary>
    /// Performs a standard edit verb on the text editing control that holds managed keyboard focus. Returns
    /// true when such a control took the verb, and false for a verb it does not offer or when no text
    /// control has focus.
    /// </summary>
    bool TryPerformTextEditing(EditIntent intent);

    /// <summary>
    /// Moves managed keyboard focus off the focused text editing control to the next or previous tab stop,
    /// as a Tab press would. Returns false when no text control has focus.
    /// </summary>
    bool TryMoveFocusFromTextControl(bool backwards);

    /// <summary>
    /// Moves the caret in the text editing control that holds managed keyboard focus. Returns true when such
    /// a control took the motion, and false when no text control has focus. Extending keeps the far end of
    /// the current selection anchored, so the shifted chords select rather than move.
    /// </summary>
    bool TryMoveCaret(CaretMotion motion, bool extendSelection);

    /// <summary>
    /// Gives up managed keyboard focus, so the keys the platform routes through the managed tree reach no
    /// control. A no-op on heads where hosted web views participate in managed focus, and when managed
    /// focus has already been given up.
    /// </summary>
    void Yield();
}
