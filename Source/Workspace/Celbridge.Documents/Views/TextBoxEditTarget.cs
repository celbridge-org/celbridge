using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Logging;
using Celbridge.Workspace;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Documents.Views;

/// <summary>
/// The edit target for a document shown as a single text box. Every verb acts on the box's own selection.
/// </summary>
public sealed class TextBoxEditTarget : IEditTarget
{
    private readonly TextBox _textBox;
    private readonly ICommandService _commandService;
    private readonly ILogger<TextBoxEditTarget> _logger;

    public TextBoxEditTarget(TextBox textBox)
    {
        _textBox = textBox;
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _logger = ServiceLocator.AcquireService<ILogger<TextBoxEditTarget>>();
    }

    public bool CanPerformEdit(EditIntent intent)
    {
        var hasSelection = _textBox.SelectionLength > 0;
        var isWritable = !_textBox.IsReadOnly;

        return intent switch
        {
            EditIntent.Copy => hasSelection,
            EditIntent.Cut => hasSelection && isWritable,
            EditIntent.Paste => isWritable,
            EditIntent.SelectAll => _textBox.Text.Length > 0,
            EditIntent.Undo => isWritable && _textBox.CanUndo,
            EditIntent.Redo => isWritable && _textBox.CanRedo,
            _ => false
        };
    }

    public void PerformEdit(EditIntent intent)
    {
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

    // Replaces the selection with the clipboard text. Reading the clipboard is async, so this finishes after
    // PerformEdit has returned.
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
