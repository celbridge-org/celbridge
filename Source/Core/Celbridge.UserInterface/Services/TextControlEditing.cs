using System.Runtime.CompilerServices;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Edits whichever managed text control holds the keyboard, keeping a record of the clipboard edits it
/// performs so that undo can reverse them.
/// </summary>
public class TextControlEditing : ITextControlEditing
{
    private readonly IManagedFocus _managedFocus;
    private readonly ConditionalWeakTable<TextBox, TextEditHistory> _textEditHistories = new();

    public TextControlEditing(IManagedFocus managedFocus)
    {
        _managedFocus = managedFocus;
    }

    public bool IsTextControlFocused => _managedFocus.FocusedElement is TextBox;

    public bool CanPerformEdit(EditIntent intent)
    {
        if (_managedFocus.FocusedElement is not TextBox textBox)
        {
            return false;
        }

        // Not gated on CanUndo or CanRedo: Uno reports both false right after typing on the Skia head, while
        // Undo still reverts the text. A chord with nothing to revert is still the control's to swallow.
        return intent switch
        {
            EditIntent.Undo or EditIntent.Redo or EditIntent.SelectAll => true,
            EditIntent.Paste => !textBox.IsReadOnly,
            EditIntent.Copy => textBox.SelectionLength > 0,
            EditIntent.Cut => textBox.SelectionLength > 0 && !textBox.IsReadOnly,
            _ => false
        };
    }

    public bool TryPerformEdit(EditIntent intent)
    {
        if (_managedFocus.FocusedElement is not TextBox textBox
            || !CanPerformEdit(intent))
        {
            return false;
        }

        switch (intent)
        {
            case EditIntent.Undo:
                UndoTextEdit(textBox);
                return true;

            case EditIntent.Redo:
                RedoTextEdit(textBox);
                return true;

            case EditIntent.SelectAll:
                textBox.SelectAll();
                return true;

            case EditIntent.Copy:
                textBox.CopySelectionToClipboard();
                return true;

            case EditIntent.Cut:
                PerformRecordedEdit(textBox, textBox.CutSelectionToClipboard);
                return true;

            case EditIntent.Paste:
                PerformRecordedEdit(textBox, textBox.PasteFromClipboard);
                return true;

            default:
                return false;
        }
    }

    // UNO-BUG: a TextBox discards its undo history on any edit that is not typing, so the clipboard edits
    // performed here leave it with nothing to reverse: undo after a cut restores nothing, and undo after a
    // paste drops back past the paste to whatever was typed before it. Recording them here is what makes
    // them reversible, and the undo and redo paths below consult that record before the control's own.
    private void PerformRecordedEdit(TextBox textBox, Action performEdit)
    {
        var before = CaptureTextEdit(textBox);
        var history = GetTextEditHistory(textBox);
        var isRecorded = false;

        // Cut changes the text before it returns, while paste reads the clipboard asynchronously and lands
        // some time later, so the edit is recorded from whichever of the two arrives.
        void RecordIfChanged()
        {
            var after = CaptureTextEdit(textBox);
            if (isRecorded
                || after.Text == before.Text)
            {
                return;
            }

            isRecorded = true;
            textBox.TextChanged -= OnTextChanged;
            history.Record(before, after);
        }

        void OnTextChanged(object sender, TextChangedEventArgs args)
        {
            RecordIfChanged();
        }

        textBox.TextChanged += OnTextChanged;
        performEdit();
        RecordIfChanged();
    }

    private void UndoTextEdit(TextBox textBox)
    {
        if (GetTextEditHistory(textBox).TryUndo(textBox.Text, out var restored))
        {
            RestoreTextEdit(textBox, restored);
            return;
        }

        textBox.Undo();
    }

    private void RedoTextEdit(TextBox textBox)
    {
        if (GetTextEditHistory(textBox).TryRedo(textBox.Text, out var restored))
        {
            RestoreTextEdit(textBox, restored);
            return;
        }

        textBox.Redo();
    }

    private TextEditHistory GetTextEditHistory(TextBox textBox)
    {
        return _textEditHistories.GetValue(textBox, _ => new TextEditHistory());
    }

    private static TextEditSnapshot CaptureTextEdit(TextBox textBox)
    {
        return new TextEditSnapshot(textBox.Text, textBox.SelectionStart, textBox.SelectionLength);
    }

    private static void RestoreTextEdit(TextBox textBox, TextEditSnapshot snapshot)
    {
        textBox.Text = snapshot.Text;

        var selectionStart = Math.Clamp(snapshot.SelectionStart, 0, snapshot.Text.Length);
        var selectionLength = Math.Clamp(snapshot.SelectionLength, 0, snapshot.Text.Length - selectionStart);

        textBox.Select(selectionStart, selectionLength);
    }

    public bool TryMoveFocusFromTextControl(bool backwards)
    {
        if (_managedFocus.FocusedElement is not TextBox textBox)
        {
            return false;
        }

        FocusNavigationHelper.MoveFocus(textBox, backwards);
        return true;
    }

    public bool TryMoveCaret(CaretMotion motion, bool extendSelection)
    {
        if (_managedFocus.FocusedElement is not TextBox textBox)
        {
            return false;
        }

        var text = textBox.Text ?? string.Empty;

        // The caret sits at the far end of the selection from the anchor, which is where a shifted chord
        // grows or shrinks the selection from.
        var anchor = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var caret = Math.Clamp(anchor + textBox.SelectionLength, 0, text.Length);
        var target = ResolveCaretTarget(text, caret, motion);

        if (extendSelection)
        {
            textBox.SelectionStart = Math.Min(anchor, target);
            textBox.SelectionLength = Math.Abs(target - anchor);
        }
        else
        {
            textBox.SelectionStart = target;
            textBox.SelectionLength = 0;
        }

        return true;
    }

    // The offset a motion lands on. The line motions stop at a line break rather than running to the ends
    // of a multi-line box.
    internal static int ResolveCaretTarget(string text, int caret, CaretMotion motion)
    {
        switch (motion)
        {
            case CaretMotion.DocumentStart:
                return 0;

            case CaretMotion.DocumentEnd:
                return text.Length;

            case CaretMotion.LineStart:
                return caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;

            case CaretMotion.LineEnd:
                var lineBreak = text.IndexOf('\n', caret);
                if (lineBreak < 0)
                {
                    return text.Length;
                }

                // A CRLF break leaves the caret before the carriage return, which is where the line's text
                // actually ends.
                return lineBreak > 0 && text[lineBreak - 1] == '\r' ? lineBreak - 1 : lineBreak;

            default:
                return caret;
        }
    }
}
