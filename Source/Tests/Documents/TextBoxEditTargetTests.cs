using Celbridge.Documents.Views;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

[TestFixture]
public class TextBoxEditTargetTests
{
    // A writable box with a selection, some text, and undo, redo and clipboard text all available, so each
    // test turns off only the one thing it is about.
    private static TextBoxEditState EditableState()
    {
        return new TextBoxEditState(
            SelectionLength: 4,
            TextLength: 20,
            IsReadOnly: false,
            CanUndo: true,
            CanRedo: true,
            HasClipboardText: true);
    }

    [Test]
    public void CanPerformEdit_EditableTextBox_AllowsEveryVerb()
    {
        var state = EditableState();

        TextBoxEditTarget.CanPerformEdit(EditIntent.Copy, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Cut, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Paste, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.SelectAll, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Undo, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Redo, state).Should().BeTrue();
    }

    [Test]
    public void CanPerformEdit_ReadOnlyTextBox_AllowsOnlyTheVerbsThatDoNotWrite()
    {
        var state = EditableState() with { IsReadOnly = true };

        TextBoxEditTarget.CanPerformEdit(EditIntent.Copy, state).Should().BeTrue();
        TextBoxEditTarget.CanPerformEdit(EditIntent.SelectAll, state).Should().BeTrue();

        TextBoxEditTarget.CanPerformEdit(EditIntent.Cut, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Paste, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Undo, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Redo, state).Should().BeFalse();
    }

    [Test]
    public void CanPerformEdit_NoSelection_RefusesCopyAndCut()
    {
        var state = EditableState() with { SelectionLength = 0 };

        TextBoxEditTarget.CanPerformEdit(EditIntent.Copy, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Cut, state).Should().BeFalse();

        // Pasting over an empty selection inserts at the caret, so it stays available.
        TextBoxEditTarget.CanPerformEdit(EditIntent.Paste, state).Should().BeTrue();
    }

    [Test]
    public void CanPerformEdit_EmptyTextBox_RefusesSelectAll()
    {
        var state = EditableState() with { SelectionLength = 0, TextLength = 0 };

        TextBoxEditTarget.CanPerformEdit(EditIntent.SelectAll, state).Should().BeFalse();
    }

    [Test]
    public void CanPerformEdit_ClipboardWithoutText_RefusesPaste()
    {
        var state = EditableState() with { HasClipboardText = false };

        TextBoxEditTarget.CanPerformEdit(EditIntent.Paste, state).Should().BeFalse();
    }

    [Test]
    public void CanPerformEdit_NothingToUndoOrRedo_RefusesThoseVerbs()
    {
        var state = EditableState() with { CanUndo = false, CanRedo = false };

        TextBoxEditTarget.CanPerformEdit(EditIntent.Undo, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Redo, state).Should().BeFalse();
    }

    [Test]
    public void CanPerformEdit_ExplorerOnlyVerbs_AreRefused()
    {
        var state = EditableState();

        // Delete, Duplicate and Rename are resource verbs the Explorer offers, not text ones.
        TextBoxEditTarget.CanPerformEdit(EditIntent.Delete, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Duplicate, state).Should().BeFalse();
        TextBoxEditTarget.CanPerformEdit(EditIntent.Rename, state).Should().BeFalse();
    }
}
