using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where the macOS caret chords land in a managed text field. Uno's TextBox binds the Windows caret
/// keys only, so these motions are the application's own and the line cases are the ones with edges: a
/// multi-line field must stop at the line break rather than running to the ends of the text.
/// </summary>
[TestFixture]
public class CaretMotionTests
{
    [Test]
    public void LineEnd_SingleLine_LandsAtTheEndOfTheText()
    {
        TextControlEditing.ResolveCaretTarget("abcdef", 3, CaretMotion.LineEnd).Should().Be(6);
    }

    [Test]
    public void LineStart_SingleLine_LandsAtTheStartOfTheText()
    {
        TextControlEditing.ResolveCaretTarget("abcdef", 3, CaretMotion.LineStart).Should().Be(0);
    }

    [Test]
    public void LineEnd_MultiLine_StopsAtTheLineBreak()
    {
        // Caret on "two": the motion ends that line rather than running to the end of the field.
        TextControlEditing.ResolveCaretTarget("one\ntwo\nthree", 5, CaretMotion.LineEnd).Should().Be(7);
    }

    [Test]
    public void LineStart_MultiLine_StopsAfterThePrecedingLineBreak()
    {
        TextControlEditing.ResolveCaretTarget("one\ntwo\nthree", 5, CaretMotion.LineStart).Should().Be(4);
    }

    [Test]
    public void LineEnd_CarriageReturnLineFeed_StopsBeforeTheCarriageReturn()
    {
        TextControlEditing.ResolveCaretTarget("one\r\ntwo", 1, CaretMotion.LineEnd).Should().Be(3);
    }

    [Test]
    public void LineStart_CaretAlreadyAtTheStart_StaysPut()
    {
        TextControlEditing.ResolveCaretTarget("one\ntwo", 0, CaretMotion.LineStart).Should().Be(0);
    }

    [Test]
    public void DocumentMotions_IgnoreLineBreaks()
    {
        TextControlEditing.ResolveCaretTarget("one\ntwo\nthree", 5, CaretMotion.DocumentStart).Should().Be(0);
        TextControlEditing.ResolveCaretTarget("one\ntwo\nthree", 5, CaretMotion.DocumentEnd).Should().Be(13);
    }

    [Test]
    public void LineEnd_EmptyText_LandsAtZero()
    {
        TextControlEditing.ResolveCaretTarget(string.Empty, 0, CaretMotion.LineEnd).Should().Be(0);
    }
}
