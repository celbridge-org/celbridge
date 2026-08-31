using Celbridge.Documents.Views;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Unit tests for ActiveDocumentFocusPolicy, the rules deciding whether a focus report makes its document
/// active, and whether a change of active document carries the keyboard to that document.
/// </summary>
[TestFixture]
public class ActiveDocumentFocusPolicyTests
{
    private static readonly ResourceKey Document = new("Notes.md");
    private static readonly ResourceKey OtherDocument = new("Report.md");

    [Test]
    public void AFocusReportNamingAnotherDocument_MakesItActive()
    {
        var shouldActivate = ActiveDocumentFocusPolicy.ShouldActivate(Document, OtherDocument);

        shouldActivate.Should().BeTrue();
    }

    [Test]
    public void AFocusReportNamingTheActiveDocument_LeavesItAlone()
    {
        // Every focus move between the controls inside a document reports that document again, so
        // activating on each one would reselect its tab and re-broadcast for a move that changed nothing.
        var shouldActivate = ActiveDocumentFocusPolicy.ShouldActivate(Document, Document);

        shouldActivate.Should().BeFalse();
    }

    [Test]
    public void AFocusReportNamingNoDocument_ActivatesNothing()
    {
        var shouldActivate = ActiveDocumentFocusPolicy.ShouldActivate(ResourceKey.Empty, Document);

        shouldActivate.Should().BeFalse();
    }

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
