using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the host's record of the edits it makes to a text control through its clipboard API.
/// The history is a pure function of the states it was handed, so these run on every platform.
/// </summary>
[TestFixture]
public class TextEditHistoryTests
{
    private static TextEditSnapshot Snapshot(string text)
    {
        return new TextEditSnapshot(text, text.Length, 0);
    }

    [Test]
    public void TryUndo_AfterAnEdit_RestoresTheStateBeforeIt()
    {
        var history = new TextEditHistory();
        var before = new TextEditSnapshot("alpha", 0, 5);
        history.Record(before, Snapshot("bravo"));

        history.TryUndo("bravo", out var restored).Should().BeTrue();
        restored.Should().Be(before);
    }

    [Test]
    public void TryUndo_WithNothingRecorded_DeclinesTheStep()
    {
        var history = new TextEditHistory();

        history.TryUndo("alpha", out _).Should().BeFalse();
    }

    [Test]
    public void TryUndo_WhenTheControlHasChangedSince_DeclinesTheStep()
    {
        var history = new TextEditHistory();
        history.Record(Snapshot("alpha"), Snapshot("bravo"));

        // Typing after the recorded edit makes the next undo the control's own to perform.
        history.TryUndo("bravoX", out _).Should().BeFalse();
    }

    [Test]
    public void TryUndo_WhenTheControlComesBackToTheRecordedState_TakesTheStep()
    {
        var history = new TextEditHistory();
        var before = Snapshot("alpha");
        history.Record(before, Snapshot("bravo"));

        history.TryUndo("bravoX", out _).Should().BeFalse();

        // The control has undone its own typing, so the recorded edit is the last thing left.
        history.TryUndo("bravo", out var restored).Should().BeTrue();
        restored.Should().Be(before);
    }

    [Test]
    public void TryUndo_AfterSeveralEdits_ReversesThemMostRecentFirst()
    {
        var history = new TextEditHistory();
        history.Record(Snapshot("alpha"), Snapshot("bravo"));
        history.Record(Snapshot("bravo"), Snapshot("charlie"));

        history.TryUndo("charlie", out var first).Should().BeTrue();
        first.Text.Should().Be("bravo");

        history.TryUndo("bravo", out var second).Should().BeTrue();
        second.Text.Should().Be("alpha");

        history.TryUndo("alpha", out _).Should().BeFalse();
    }

    [Test]
    public void TryRedo_AfterAnUndo_ReappliesTheEdit()
    {
        var history = new TextEditHistory();
        var after = new TextEditSnapshot("bravo", 5, 0);
        history.Record(Snapshot("alpha"), after);
        history.TryUndo("bravo", out _);

        history.TryRedo("alpha", out var restored).Should().BeTrue();
        restored.Should().Be(after);

        // The reapplied edit is undoable again.
        history.TryUndo("bravo", out _).Should().BeTrue();
    }

    [Test]
    public void TryRedo_WhenTheControlHasChangedSinceTheUndo_DeclinesTheStep()
    {
        var history = new TextEditHistory();
        history.Record(Snapshot("alpha"), Snapshot("bravo"));
        history.TryUndo("bravo", out _);

        history.TryRedo("alphaX", out _).Should().BeFalse();
    }

    [Test]
    public void Record_AfterAnUndo_DropsWhatWasUndone()
    {
        var history = new TextEditHistory();
        history.Record(Snapshot("alpha"), Snapshot("bravo"));
        history.TryUndo("bravo", out _);

        history.Record(Snapshot("alpha"), Snapshot("delta"));

        history.TryRedo("alpha", out _).Should().BeFalse();
    }
}
