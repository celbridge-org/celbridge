using Celbridge.Automation;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;

namespace Celbridge.Tests.Commands;

[TestFixture]
public class PerformEditCommandTests
{
    private IFocusService _focusService = null!;
    private IAutomationService _automationService = null!;

    [SetUp]
    public void SetUp()
    {
        _focusService = Substitute.For<IFocusService>();
        _automationService = Substitute.For<IAutomationService>();
    }

    [Test]
    public async Task ExecuteAsync_RoutesIntentToEditTarget_WhenItCanPerformEdit()
    {
        var target = Substitute.For<IEditTarget>();
        target.CanPerformEdit(EditIntent.Copy).Returns(true);
        _focusService.EditTarget.Returns(target);

        var command = new PerformEditCommand(_focusService, _automationService) { Intent = EditIntent.Copy };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        target.Received(1).PerformEdit(EditIntent.Copy);
        _automationService.Received(1).LogEvent(new EditCommandPerformedEvent(EditIntent.Copy));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotExecute_WhenTargetCannotExecute()
    {
        var target = Substitute.For<IEditTarget>();
        target.CanPerformEdit(EditIntent.Copy).Returns(false);
        _focusService.EditTarget.Returns(target);

        var command = new PerformEditCommand(_focusService, _automationService) { Intent = EditIntent.Copy };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        target.DidNotReceive().PerformEdit(Arg.Any<EditIntent>());
        _automationService.DidNotReceive().LogEvent(Arg.Any<AutomationEvent>());
    }

    [Test]
    public async Task ExecuteAsync_NoEditTarget_IsNoOp()
    {
        _focusService.EditTarget.Returns((IEditTarget?)null);

        var command = new PerformEditCommand(_focusService, _automationService) { Intent = EditIntent.Paste };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
    }
}
