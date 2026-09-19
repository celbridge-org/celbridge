namespace Celbridge.UserInterface.Services;

/// <summary>
/// The content and selection of a text control at one moment.
/// </summary>
public sealed record TextEditSnapshot(string Text, int SelectionStart, int SelectionLength);

// UNO-BUG: a TextBox records only typed input in its undo history. An edit made through its own clipboard
// API, or by assigning SelectedText, leaves no entry, so the control cannot reverse it.
/// <summary>
/// Undo history for the edits the host performs on a text control through its clipboard API. The control
/// records its own typing and nothing else, so an edit made this way is reversible only if the host keeps
/// its own record of it.
/// </summary>
public sealed class TextEditHistory
{
    private const int MaximumEntries = 50;

    private readonly List<TextEditEntry> _undoEntries = new();
    private readonly List<TextEditEntry> _redoEntries = new();

    /// <summary>
    /// Records an edit that took the control from one state to another.
    /// </summary>
    public void Record(TextEditSnapshot before, TextEditSnapshot after)
    {
        _redoEntries.Clear();
        _undoEntries.Add(new TextEditEntry(before, after));

        if (_undoEntries.Count > MaximumEntries)
        {
            _undoEntries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Returns the state to restore to reverse the most recent recorded edit, if that edit is still the
    /// last thing to have happened to the control. False hands the step back to the control's own history.
    /// </summary>
    public bool TryUndo(string currentText, out TextEditSnapshot restored)
    {
        restored = default!;

        if (_undoEntries.Count == 0)
        {
            return false;
        }

        // Typing since the recorded edit leaves text the edit did not produce, and that step belongs to the
        // control's own history. The entry stays put, ready for the undo that comes back round to it.
        var entry = _undoEntries[^1];
        if (entry.After.Text != currentText)
        {
            return false;
        }

        _undoEntries.RemoveAt(_undoEntries.Count - 1);
        _redoEntries.Add(entry);
        restored = entry.Before;

        return true;
    }

    /// <summary>
    /// Returns the state to restore to reapply the most recently undone edit, if the control is still in
    /// the state that undo left it in.
    /// </summary>
    public bool TryRedo(string currentText, out TextEditSnapshot restored)
    {
        restored = default!;

        if (_redoEntries.Count == 0)
        {
            return false;
        }

        var entry = _redoEntries[^1];
        if (entry.Before.Text != currentText)
        {
            return false;
        }

        _redoEntries.RemoveAt(_redoEntries.Count - 1);
        _undoEntries.Add(entry);
        restored = entry.After;

        return true;
    }

    private sealed record TextEditEntry(TextEditSnapshot Before, TextEditSnapshot After);
}
