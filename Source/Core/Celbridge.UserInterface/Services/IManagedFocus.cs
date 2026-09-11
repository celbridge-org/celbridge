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
/// The managed keyboard focus of the window: whether a popup currently holds it, and the means to give it
/// up so no managed control claims keys destined for a focused web surface.
/// </summary>
public interface IManagedFocus
{
    /// <summary>
    /// True while managed keyboard focus rests inside a popup: a flyout, a context menu or a content
    /// dialog. Answered from the focused element each time it is asked, so it cannot go stale.
    /// </summary>
    bool IsPopupHoldingFocus { get; }

    /// <summary>
    /// Performs undo or redo on the text editing control that holds managed keyboard focus. Returns true when
    /// such a control took the verb, and false for any other verb or when no text control has focus.
    /// </summary>
    bool TryPerformTextEditing(EditIntent intent);

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
