using Celbridge.Commands;
using Celbridge.UserInterface.Platform;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the router that decides who performs a standard edit verb. Resolution is a pure function
/// of the focused surface, so these run on every platform.
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

    [Test]
    public void Resolve_WhenTheSurfaceCanPerformTheVerb_GivesItToTheSurface()
    {
        var editTarget = CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll, EditIntent.Copy);
        var focusService = CreateFocusService(editTarget);

        MacOSEditCommands.Resolve(EditIntent.SelectAll, focusService).Should().Be(EditRouting.Surface);
        MacOSEditCommands.Resolve(EditIntent.Copy, focusService).Should().Be(EditRouting.Surface);
    }

    [TestCase(EditIntent.Cut)]
    [TestCase(EditIntent.Copy)]
    [TestCase(EditIntent.Paste)]
    public void Resolve_ForAnUnavailableVerbOnAMediatedClipboard_GivesItToNobody(EditIntent intent)
    {
        // AppKit's own cut: would change the page without telling the editor, so an unavailable verb stays
        // unavailable.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        MacOSEditCommands.Resolve(intent, focusService).Should().Be(EditRouting.Unavailable);
    }

    [TestCase(EditIntent.Cut)]
    [TestCase(EditIntent.Copy)]
    [TestCase(EditIntent.Paste)]
    public void Resolve_ForAClipboardVerbOnAnUnmediatedSurface_GivesItToTheResponderChain(EditIntent intent)
    {
        // A rich text editor needs the responder chain's native clipboard handling, which keeps formatting.
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));

        MacOSEditCommands.Resolve(intent, focusService).Should().Be(EditRouting.ResponderChain);
    }

    [TestCase(EditIntent.Undo)]
    [TestCase(EditIntent.Redo)]
    [TestCase(EditIntent.SelectAll)]
    public void Resolve_ForANonClipboardVerbTheSurfaceCannotPerform_GivesItToTheResponderChain(EditIntent intent)
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));

        MacOSEditCommands.Resolve(intent, focusService).Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Resolve_WithNoFocusedSurface_GivesTheVerbToTheResponderChain()
    {
        MacOSEditCommands.Resolve(EditIntent.Paste, CreateFocusService(null)).Should().Be(EditRouting.ResponderChain);
        MacOSEditCommands.Resolve(EditIntent.Paste, focusService: null).Should().Be(EditRouting.ResponderChain);
    }

    [Test]
    public void Perform_WhenTheSurfaceCanPerformTheVerb_ExecutesTheEditCommand()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true, EditIntent.SelectAll));
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.SelectAll, focusService, commandService)
            .Should().Be(EditRouting.Surface);

        commandService.ReceivedWithAnyArgs(1).Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_ForAnUnavailableVerbOnAMediatedClipboard_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: true));
        var commandService = Substitute.For<ICommandService>();

        // The caller stops here, and no edit command runs.
        MacOSEditCommands.Perform(EditIntent.Cut, focusService, commandService)
            .Should().Be(EditRouting.Unavailable);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }

    [Test]
    public void Perform_ForAVerbTheResponderChainOwns_RunsNothing()
    {
        var focusService = CreateFocusService(CreateEditTarget(hostMediatedClipboard: false));
        var commandService = Substitute.For<ICommandService>();

        MacOSEditCommands.Perform(EditIntent.Paste, focusService, commandService)
            .Should().Be(EditRouting.ResponderChain);

        commandService.DidNotReceiveWithAnyArgs().Execute<IPerformEditCommand>();
    }
}
