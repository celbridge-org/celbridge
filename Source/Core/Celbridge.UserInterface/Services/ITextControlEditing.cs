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
/// The edits the application performs on the managed text control that holds the keyboard, such as a
/// dialog's field: the standard edit verbs, caret motion, and moving focus on as Tab does. Each member
/// acts on whichever text control holds managed keyboard focus when it is called.
/// </summary>
public interface ITextControlEditing
{
    /// <summary>
    /// Whether a text editing control holds managed keyboard focus.
    /// </summary>
    bool IsTextControlFocused { get; }

    /// <summary>
    /// Whether the focused text control can perform the verb right now. False when no text control has
    /// focus.
    /// </summary>
    bool CanPerformEdit(EditIntent intent);

    /// <summary>
    /// Performs a standard edit verb on the focused text control. Returns true when it took the verb, and
    /// false for a verb it does not offer or when no text control has focus.
    /// </summary>
    bool TryPerformEdit(EditIntent intent);

    /// <summary>
    /// Moves managed keyboard focus off the focused text control to the next or previous tab stop, as a Tab
    /// press would. Returns false when no text control has focus.
    /// </summary>
    bool TryMoveFocusFromTextControl(bool backwards);

    /// <summary>
    /// Moves the caret in the focused text control. Returns true when it took the motion, and false when no
    /// text control has focus. Extending keeps the far end of the current selection anchored, so the shifted
    /// chords select rather than move.
    /// </summary>
    bool TryMoveCaret(CaretMotion motion, bool extendSelection);
}
