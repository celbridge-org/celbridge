using Celbridge.Documents.Views;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Unit tests for ActiveDocumentFocusPolicy, the rule deciding whether a change of active document
/// carries the keyboard to that document.
/// </summary>
[TestFixture]
public class ActiveDocumentFocusPolicyTests
{
    private static readonly ResourceKey Document = new("Notes.md");

    [Test]
    public void AnActivatedDocument_TakesTheKeyboard()
    {
        var shouldCarryFocus = ActiveDocumentFocusPolicy.ShouldCarryFocus(
            Document,
            ActiveDocumentChangeReason.Activated);

        shouldCarryFocus.Should().BeTrue();
    }

    [Test]
    public void ADocumentMadeActiveByItsOwnFocus_DoesNotTakeTheKeyboardAgain()
    {
        // Two web surfaces trading focus without settling is what this prevents: a grant reports focus, the
        // report makes that document active, and an activation that granted focus would start the next lap.
        var shouldCarryFocus = ActiveDocumentFocusPolicy.ShouldCarryFocus(
            Document,
            ActiveDocumentChangeReason.Focused);

        shouldCarryFocus.Should().BeFalse();
    }

    [Test]
    public void ARestoredDocument_DoesNotTakeTheKeyboard()
    {
        var shouldCarryFocus = ActiveDocumentFocusPolicy.ShouldCarryFocus(
            Document,
            ActiveDocumentChangeReason.Restored);

        shouldCarryFocus.Should().BeFalse();
    }

    [Test]
    public void TheLastDocumentClosing_CarriesFocusNowhere()
    {
        var shouldCarryFocus = ActiveDocumentFocusPolicy.ShouldCarryFocus(
            ResourceKey.Empty,
            ActiveDocumentChangeReason.Activated);

        shouldCarryFocus.Should().BeFalse();
    }
}
