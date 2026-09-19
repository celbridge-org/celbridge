using System.Runtime.CompilerServices;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Managed keyboard focus for the window. Focus is given up by moving it onto an inert zero-sized
/// placeholder in the window root, because WinUI has no way to express that no control has focus.
/// </summary>
public class ManagedFocus : IManagedFocus
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IPlatformInfo _platformInfo;
    private readonly ILogger<ManagedFocus> _logger;
    private readonly ConditionalWeakTable<TextBox, TextEditHistory> _textEditHistories = new();

    private ContentControl? _placeholder;
    private bool _reportedFocusFailure;

    public ManagedFocus(
        IUserInterfaceService userInterfaceService,
        IPlatformInfo platformInfo,
        ILogger<ManagedFocus> logger)
    {
        _userInterfaceService = userInterfaceService;
        _platformInfo = platformInfo;
        _logger = logger;
    }

    public FocusLocation FocusLocation
    {
        get
        {
            var focusedElement = GetFocusedElement();

            // Nothing focused is nothing stranded, so it reads as the main content.
            return focusedElement is null
                ? FocusLocation.MainContent
                : FocusTracking.GetFocusLocation(focusedElement);
        }
    }

    public bool IsTextControlFocused => GetFocusedElement() is TextBox;

    public bool CanPerformTextEditing(EditIntent intent)
    {
        if (GetFocusedElement() is not TextBox textBox)
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

    public bool TryPerformTextEditing(EditIntent intent)
    {
        if (GetFocusedElement() is not TextBox textBox
            || !CanPerformTextEditing(intent))
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
        if (GetFocusedElement() is not TextBox textBox)
        {
            return false;
        }

        FocusNavigationHelper.MoveFocus(textBox, backwards);
        return true;
    }

    public bool TryMoveCaret(CaretMotion motion, bool extendSelection)
    {
        if (GetFocusedElement() is not TextBox textBox)
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

    // The offset a motion lands on. The line motions bound to the line holding the caret, so they stop at a
    // line break rather than running to the ends of a multi-line box.
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

    public void Yield()
    {
        // Yielding only means something where a web surface's native focus leaves managed focus behind. On
        // the other heads focusing the web view is itself a managed focus change, so there is nothing to
        // yield and managed focus must stay free to move to the web view.
        if (!_platformInfo.HostedWebViewFocusIsNative)
        {
            return;
        }

        var placeholder = _placeholder ??= CreatePlaceholder();
        if (placeholder is null)
        {
            return;
        }

        // Re-applying managed focus the placeholder already holds makes Uno resign the web surface's
        // native focus again, which the first responder monitor reconciles by yielding again, looping.
        if (ReferenceEquals(GetFocusedElement(), placeholder))
        {
            return;
        }

        // Focus is refused outright unless the placeholder is a tab stop, so it becomes one only for the
        // moment it takes focus: a zero-sized stop left in the tab order would strand a Tab press.
        // Moving managed focus makes Uno resign the native first responder, so the page holding the caret
        // sees a blur here. Logged because that blur is indistinguishable, at the page, from the user
        // clicking away.
        _logger.LogTrace("Yielding managed focus to the placeholder");

        placeholder.IsTabStop = true;
        var focused = placeholder.Focus(FocusState.Programmatic);
        placeholder.IsTabStop = false;

        if (!focused
            && !_reportedFocusFailure)
        {
            _reportedFocusFailure = true;
            _logger.LogWarning("Managed focus could not be yielded, so keys may still reach the previously focused control");
        }
    }

    private UIElement? GetFocusedElement()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not UIElement rootContent
            || rootContent.XamlRoot is null)
        {
            return null;
        }

        return Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootContent.XamlRoot) as UIElement;
    }

    private ContentControl? CreatePlaceholder()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not Panel rootPanel)
        {
            return null;
        }

        var placeholder = new ContentControl
        {
            Width = 0,
            Height = 0,
            IsTabStop = false
        };

        // The placeholder belongs to no panel, so without this the focus tracker would classify it as a move
        // off the workspace panels and clear panel focus. Focus landing here means nothing changed.
        FocusTracking.SetPreservePanelFocus(placeholder, true);

        rootPanel.Children.Add(placeholder);

        return placeholder;
    }
}
