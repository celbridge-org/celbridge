using Celbridge.Commands;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the router that decides who performs a standard edit verb. Resolution is a pure function
/// of the focused surface and text control, so these run on every platform.
/// </summary>
[TestFixture]
public class EditVerbRouterTests
{
    private static IEnumerable<EditIntent> EveryEditIntent => Enum.GetValues<EditIntent>();

    private static IEnumerable<EditIntent> ClipboardEditIntents
    {
        get
        {
            yield return EditIntent.Cut;
            yield return EditIntent.Copy;
            yield return EditIntent.Paste;
        }
    }

    private static IEnumerable<EditIntent> NonClipboardEditIntents => EveryEditIntent.Except(ClipboardEditIntents);

    private static IFocusService CreateFocusService(IEditTarget? editTarget)
    {
        var focusService = Substitute.For<IFocusService>();
        focusService.EditTarget.Returns(editTarget);

        return focusService;
    }

    private static IEditTarget CreateEditTarget(bool hostMediatedClipboard, params EditIntent[] canPerform)
    {
        var editTarget = Substitute.For<IEditTarget>();
        editTarget.HostMediatedClipboard.Returns(hostMediatedClipboard);
        editTarget.CanPerformEdit(Arg.Any<EditIntent>()).Returns(call => canPerform.Contains(call.Arg<EditIntent>()));

        return editTarget;
    }

    // An edit target that answers for every verb, as a document editor with a selection does.
    private static IEditTarget CreateCapableEditTarget(bool hostMediatedClipboard)
    {
        return CreateEditTarget(hostMediatedClipboard, EveryEditIntent.ToArray());
    }

    // Managed focus resting on a text control that can perform the given verbs.
    private static ITextControlEditing CreateTextControlFocus(params EditIntent[] canPerform)
    {
        var textControlEditing = Substitute.For<ITextControlEditing>();
        textControlEditing.IsTextControlFocused.Returns(true);
        textControlEditing.CanPerformEdit(Arg.Any<EditIntent>())
            .Returns(call => canPerform.Contains(call.Arg<EditIntent>()));

        return textControlEditing;
    }

    private static ITextControlEditing CreateCapableTextControlFocus()
    {
        return CreateTextControlFocus(EveryEditIntent.ToArray());
    }

    // Managed focus resting anywhere but a text control.
    private static ITextControlEditing CreateNoTextControlFocus()
    {
        var textControlEditing = Substitute.For<ITextControlEditing>();
        textControlEditing.IsTextControlFocused.Returns(false);

        return textControlEditing;
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WhenTheSurfaceCanPerformTheVerb_GivesItToTheSurface(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateCapableEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.Surface);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WhenTheSurfaceCanPerformTheVerbWithATextControlFocused_GivesItToTheSurface(EditIntent intent)
    {
        // The surface owns the verb, so a text control in its chrome does not take it from the content.
        var focusService = CreateFocusService(CreateCapableEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateCapableTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.Surface);
    }

    [TestCaseSource(nameof(ClipboardEditIntents))]
    public void Resolve_ForAnUnavailableVerbOnAMediatedClipboard_GivesItToNobody(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.Unavailable);
    }

    [TestCaseSource(nameof(NonClipboardEditIntents))]
    public void Resolve_ForANonClipboardVerbTheSurfaceCannotPerform_GivesItToTheResponderChain(EditIntent intent)
    {
        // Only the clipboard verbs are mediated, so the rest are still the platform's to answer.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_ForAVerbTheSurfaceCannotPerformOnAnUnmediatedClipboard_GivesItToTheResponderChain(EditIntent intent)
    {
        // A rich text editor needs the responder chain's native clipboard handling, which keeps formatting.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        EditVerbRouter.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_ForAVerbTheSurfaceCannotPerformWithATextControlFocused_GivesItToTheTextControl(EditIntent intent)
    {
        // A field in a panel's chrome is edited while the panel's own edit target answers for nothing.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        EditVerbRouter.Resolve(intent, focusService, CreateTextControlFocus(intent), isDialogOpen: false)
            .Should().Be(EditRouting.TextControl);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_ForAVerbTheFocusedTextControlCannotPerform_GivesItToNobody(EditIntent intent)
    {
        // A text control holding the keyboard owns every standard verb, so Copy with nothing selected is
        // unavailable and must not reach the panel behind it through the responder chain.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        EditVerbRouter.Resolve(intent, focusService, CreateTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.Unavailable);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WithNothingFocused_GivesTheVerbToTheResponderChain(EditIntent intent)
    {
        EditVerbRouter.Resolve(intent, CreateFocusService(null), CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WhileADialogHoldsTheKeyboard_GivesTheVerbToItsTextControl(EditIntent intent)
    {
        // The focus service still names the panel behind the dialog, whose edit target would otherwise
        // act on a verb the user aimed at the dialog's own text box.
        var focusService = CreateFocusService(CreateCapableEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateTextControlFocus(intent), isDialogOpen: true)
            .Should().Be(EditRouting.TextControl);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WhileADialogHoldsTheKeyboardForAVerbItsTextControlCannotPerform_GivesItToNobody(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateCapableEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateTextControlFocus(), isDialogOpen: true)
            .Should().Be(EditRouting.Unavailable);
    }

    [TestCaseSource(nameof(EveryEditIntent))]
    public void Resolve_WhileADialogHoldsTheKeyboardWithNoTextControlFocused_GivesTheVerbToTheResponderChain(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateCapableEditTarget(hostMediatedClipboard: true));

        EditVerbRouter.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: true)
            .Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Resolve_WithNoFocusServiceAtAll_GivesTheVerbToTheResponderChain()
    {
        // A native panel such as a file picker holds the keyboard, so neither service is offered.
        EditVerbRouter.Resolve(EditIntent.Paste, focusService: null, textControlEditing: null, isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Perform_WhenTheSurfaceCanPerformTheVerb_ExecutesTheEditCommand()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll));
        var commandService = Substitute.For<ICommandService>();

        EditVerbRouter.Perform(EditIntent.SelectAll, focusService, CreateNoTextControlFocus(), commandService, isDialogOpen: false)
            .Should().Be(EditRouting.Surface);

        commandService.ReceivedWithAnyArgs(1).Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_WhenTheFocusedTextControlOwnsTheVerb_PerformsItThere()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll));
        var textControlEditing = CreateTextControlFocus(EditIntent.SelectAll);
        var commandService = Substitute.For<ICommandService>();

        EditVerbRouter.Perform(EditIntent.SelectAll, focusService, textControlEditing, commandService, isDialogOpen: true)
            .Should().Be(EditRouting.TextControl);

        textControlEditing.Received(1).TryPerformEdit(EditIntent.SelectAll);
        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_ForAnUnavailableVerbOnAMediatedClipboard_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));
        var textControlEditing = CreateNoTextControlFocus();
        var commandService = Substitute.For<ICommandService>();

        EditVerbRouter.Perform(EditIntent.Cut, focusService, textControlEditing, commandService, isDialogOpen: false)
            .Should().Be(EditRouting.Unavailable);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
        textControlEditing.DidNotReceiveWithAnyArgs().TryPerformEdit(default);
    }

    [Test]
    public void Perform_ForAVerbTheResponderChainOwns_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));
        var textControlEditing = CreateNoTextControlFocus();
        var commandService = Substitute.For<ICommandService>();

        EditVerbRouter.Perform(EditIntent.Paste, focusService, textControlEditing, commandService, isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
        textControlEditing.DidNotReceiveWithAnyArgs().TryPerformEdit(default);
    }
}
