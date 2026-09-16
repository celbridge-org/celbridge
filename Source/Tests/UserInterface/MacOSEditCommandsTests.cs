using Celbridge.Commands;
using Celbridge.UserInterface.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the router that decides who performs a standard edit verb. Resolution is a pure function
/// of the focused surface and text control, so these run on every platform.
/// </summary>
[TestFixture]
public class MacOSEditCommandsTests
{
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

    // Managed focus resting on a text control that can perform the given verbs.
    private static IManagedFocus CreateTextControlFocus(params EditIntent[] canPerform)
    {
        var managedFocus = Substitute.For<IManagedFocus>();
        managedFocus.IsTextControlFocused.Returns(true);
        managedFocus.CanPerformTextEditing(Arg.Any<EditIntent>())
            .Returns(call => canPerform.Contains(call.Arg<EditIntent>()));

        return managedFocus;
    }

    // Managed focus resting anywhere but a text control.
    private static IManagedFocus CreateNoTextControlFocus()
    {
        var managedFocus = Substitute.For<IManagedFocus>();
        managedFocus.IsTextControlFocused.Returns(false);

        return managedFocus;
    }

    [Test]
    public void Resolve_WhenTheSurfaceCanPerformTheVerb_GivesItToTheSurface()
    {
        var editTarget = CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll, EditIntent.Copy);
        var focusService = CreateFocusService(editTarget);
        var managedFocus = CreateNoTextControlFocus();

        MacOSEditCommands.Resolve(EditIntent.SelectAll, focusService, managedFocus, isDialogOpen: false)
            .Should().Be(EditRouting.Surface);
        MacOSEditCommands.Resolve(EditIntent.Copy, focusService, managedFocus, isDialogOpen: false)
            .Should().Be(EditRouting.Surface);
    }

    [TestCase(EditIntent.Cut)]
    [TestCase(EditIntent.Copy)]
    [TestCase(EditIntent.Paste)]
    public void Resolve_ForAnUnavailableVerbOnAMediatedClipboard_GivesItToNobody(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        MacOSEditCommands.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.Unavailable);
    }

    [TestCase(EditIntent.Cut)]
    [TestCase(EditIntent.Copy)]
    [TestCase(EditIntent.Paste)]
    public void Resolve_ForAClipboardVerbOnAnUnmediatedSurface_GivesItToTheResponderChain(EditIntent intent)
    {
        // A rich text editor needs the responder chain's native clipboard handling, which keeps formatting.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        MacOSEditCommands.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [TestCase(EditIntent.Undo)]
    [TestCase(EditIntent.Redo)]
    [TestCase(EditIntent.SelectAll)]
    public void Resolve_ForANonClipboardVerbTheSurfaceCannotPerform_GivesItToTheResponderChain(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        MacOSEditCommands.Resolve(intent, focusService, CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Resolve_WithNoFocusedSurface_GivesTheVerbToTheResponderChain()
    {
        MacOSEditCommands.Resolve(EditIntent.Paste, CreateFocusService(null), CreateNoTextControlFocus(), isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
        MacOSEditCommands.Resolve(EditIntent.Paste, focusService: null, managedFocus: null, isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Perform_WhenTheSurfaceCanPerformTheVerb_ExecutesTheEditCommand()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll));
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.SelectAll, focusService, CreateNoTextControlFocus(), commandService, isDialogOpen: false)
            .Should().Be(EditRouting.Surface);

        commandService.ReceivedWithAnyArgs(1).Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_ForAnUnavailableVerbOnAMediatedClipboard_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.Cut, focusService, CreateNoTextControlFocus(), commandService, isDialogOpen: false)
            .Should().Be(EditRouting.Unavailable);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_ForAVerbTheResponderChainOwns_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.Paste, focusService, CreateNoTextControlFocus(), commandService, isDialogOpen: false)
            .Should().Be(EditRouting.ResponderChain);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }

    [TestCase(EditIntent.SelectAll)]
    [TestCase(EditIntent.Copy)]
    [TestCase(EditIntent.Paste)]
    [TestCase(EditIntent.Undo)]
    public void Resolve_WhileADialogHoldsTheKeyboard_GivesTheVerbToItsTextControl(EditIntent intent)
    {
        // The focus service still names the panel behind the dialog, whose edit target would otherwise
        // act on a verb the user aimed at the dialog's own text box.
        var editTarget = CreateEditTarget(
            hostMediatedClipboard: true,
            EditIntent.SelectAll,
            EditIntent.Copy,
            EditIntent.Paste,
            EditIntent.Undo);
        var focusService = CreateFocusService(editTarget);
        var managedFocus = CreateTextControlFocus(intent);

        MacOSEditCommands.Resolve(intent, focusService, managedFocus, isDialogOpen: true)
            .Should().Be(EditRouting.TextControl);
    }

    [Test]
    public void Resolve_WhileADialogHoldsTheKeyboardWithNoTextControlFocused_GivesTheVerbToTheResponderChain()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.Copy));

        MacOSEditCommands.Resolve(EditIntent.Copy, focusService, CreateNoTextControlFocus(), isDialogOpen: true)
            .Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Resolve_ForAVerbTheFocusedTextControlCannotPerform_GivesItToNobody()
    {
        // A text control holding the keyboard owns every standard verb, so Copy with nothing selected is
        // unavailable and must not reach the panel behind it through the responder chain.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        MacOSEditCommands.Resolve(EditIntent.Copy, focusService, CreateTextControlFocus(), isDialogOpen: true)
            .Should().Be(EditRouting.Unavailable);
    }

    [Test]
    public void Resolve_ForAVerbTheSurfaceCannotPerformWithATextControlFocused_GivesItToTheTextControl()
    {
        // A field in a panel's chrome is edited while the panel's own edit target answers for nothing.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        MacOSEditCommands.Resolve(EditIntent.Paste, focusService, CreateTextControlFocus(EditIntent.Paste), isDialogOpen: false)
            .Should().Be(EditRouting.TextControl);
    }

    [Test]
    public void Perform_WhenTheFocusedTextControlOwnsTheVerb_PerformsItThere()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll));
        var managedFocus = CreateTextControlFocus(EditIntent.SelectAll);
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.SelectAll, focusService, managedFocus, commandService, isDialogOpen: true)
            .Should().Be(EditRouting.TextControl);

        managedFocus.Received(1).TryPerformTextEditing(EditIntent.SelectAll);
        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }
}
