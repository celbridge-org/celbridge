using Celbridge.Documents.Views;
using Celbridge.UserInterface;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Unit tests for ActiveDocumentFocusPolicy, the rules deciding whether a focus report makes its document
/// active, whether a change of active document carries the keyboard to that document, and whether a press
/// inside a document hands the keyboard to it.
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

    [Test]
    public void APressThatReachedNothingFocusable_HandsTheKeyboardToTheDocument()
    {
        var shouldFocus = ActiveDocumentFocusPolicy.ShouldFocusPressedDocument(
            focusIsInPressedDocument: false,
            FocusLocation.MainContent);

        shouldFocus.Should().BeTrue();
    }

    [Test]
    public void APressThatReachedAControlInTheDocument_LeavesTheControlFocused()
    {
        var shouldFocus = ActiveDocumentFocusPolicy.ShouldFocusPressedDocument(
            focusIsInPressedDocument: true,
            FocusLocation.MainContent);

        shouldFocus.Should().BeFalse();
    }

    [Test]
    public void APressThatOpenedADialogOrFlyout_LeavesTheKeyboardWithIt()
    {
        // Focus is judged once it settles after the press, by which time a button's click can have opened a
        // dialog or flyout and moved focus into it.
        var shouldFocus = ActiveDocumentFocusPolicy.ShouldFocusPressedDocument(
            focusIsInPressedDocument: false,
            FocusLocation.Popup);

        shouldFocus.Should().BeFalse();
    }

    [Test]
    public void APressThatLeftFocusStranded_HandsTheKeyboardToTheDocument()
    {
        // Focus a dismissed popup left behind reaches nothing on screen.
        var shouldFocus = ActiveDocumentFocusPolicy.ShouldFocusPressedDocument(
            focusIsInPressedDocument: false,
            FocusLocation.Detached);

        shouldFocus.Should().BeTrue();
    }
}
