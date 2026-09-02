using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Logging;
using Celbridge.Workspace;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Documents.Views;

/// <summary>
/// The text box and clipboard state that decides which edit verbs are available.
/// </summary>
internal readonly record struct TextBoxEditState(
    int SelectionLength,
    int TextLength,
    bool IsReadOnly,
    bool CanUndo,
    bool CanRedo,
    bool HasClipboardText);

/// <summary>
/// The edit target for a document shown as a single text box. Every verb acts on the box's own selection.
/// </summary>
public sealed class TextBoxEditTarget : IEditTarget
{
    private readonly TextBox _textBox;
    private readonly ICommandService _commandService;
    private readonly ILogger<TextBoxEditTarget> _logger;

    public TextBoxEditTarget(
        TextBox textBox,
        ICommandService commandService,
        ILogger<TextBoxEditTarget> logger)
    {
        _textBox = textBox;
        _commandService = commandService;
        _logger = logger;
    }

    public bool CanPerformEdit(EditIntent intent)
    {
        return CanPerformEdit(intent, ReadState());
    }

    // Separated from the control so the verb rules can be exercised without a live text box.
    internal static bool CanPerformEdit(EditIntent intent, TextBoxEditState state)
    {
        var hasSelection = state.SelectionLength > 0;
        var isWritable = !state.IsReadOnly;

        return intent switch
        {
            EditIntent.Copy => hasSelection,
            EditIntent.Cut => hasSelection && isWritable,
            EditIntent.Paste => isWritable && state.HasClipboardText,
            EditIntent.SelectAll => state.TextLength > 0,
            EditIntent.Undo => isWritable && state.CanUndo,
            EditIntent.Redo => isWritable && state.CanRedo,
            _ => false
        };
    }

    public void PerformEdit(EditIntent intent)
    {
        // Assigning SelectedText edits the document even when the box is read-only, so the verb is
        // re-checked here rather than trusting every caller to have asked first.
        if (!CanPerformEdit(intent))
        {
            return;
        }

        switch (intent)
        {
            case EditIntent.Copy:
                CopySelection(deleteSelection: false);
                break;

            case EditIntent.Cut:
                CopySelection(deleteSelection: true);
                break;

            case EditIntent.Paste:
                _ = PasteAsync();
                break;

            case EditIntent.SelectAll:
                _textBox.SelectAll();
                break;

            case EditIntent.Undo:
                _textBox.Undo();
                break;

            case EditIntent.Redo:
                _textBox.Redo();
                break;
        }
    }

    public bool TryHandleTabKey(bool shift)
    {
        // A plain text box does not indent, so normal focus navigation proceeds.
        return false;
    }

    private TextBoxEditState ReadState()
    {
        return new TextBoxEditState(
            _textBox.SelectionLength,
            _textBox.Text.Length,
            _textBox.IsReadOnly,
            _textBox.CanUndo,
            _textBox.CanRedo,
            HasClipboardText());
    }

    // An unreadable clipboard reports text, so Paste stays offered and does nothing when it runs.
    private bool HasClipboardText()
    {
        try
        {
            return Clipboard.GetContent().Contains(StandardDataFormats.Text);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read the clipboard");
            return true;
        }
    }

    private void CopySelection(bool deleteSelection)
    {
        var selectedText = _textBox.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        _commandService.Execute<ICopyTextToClipboardCommand>(command => command.Text = selectedText);

        if (deleteSelection)
        {
            _textBox.SelectedText = string.Empty;
        }
    }

    // Reading the clipboard is async, so this finishes after PerformEdit has returned.
    private async Task PasteAsync()
    {
        try
        {
            var clipboardContent = Clipboard.GetContent();
            if (!clipboardContent.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await clipboardContent.GetTextAsync();
            _textBox.SelectedText = text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste the clipboard into the text document");
        }
    }
}
