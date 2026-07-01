using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;

namespace Celbridge.Tests.Commands;

[TestFixture]
public class PerformEditCommandTests
{
    private IFocusService _focusService = null!;

    [SetUp]
    public void SetUp()
    {
        _focusService = Substitute.For<IFocusService>();
    }

    [Test]
    public async Task ExecuteAsync_RoutesIntentToEditTarget_WhenItCanPerformEdit()
    {
        var target = Substitute.For<IEditTarget>();
        target.CanPerformEdit(EditIntent.Copy).Returns(true);
        _focusService.EditTarget.Returns(target);

        var command = new PerformEditCommand(_focusService) { Intent = EditIntent.Copy };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        target.Received(1).PerformEdit(EditIntent.Copy);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotExecute_WhenTargetCannotExecute()
    {
        var target = Substitute.For<IEditTarget>();
        target.CanPerformEdit(EditIntent.Copy).Returns(false);
        _focusService.EditTarget.Returns(target);

        var command = new PerformEditCommand(_focusService) { Intent = EditIntent.Copy };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        target.DidNotReceive().PerformEdit(Arg.Any<EditIntent>());
    }

    [Test]
    public async Task ExecuteAsync_NoEditTarget_IsNoOp()
    {
        _focusService.EditTarget.Returns((IEditTarget?)null);

        var command = new PerformEditCommand(_focusService) { Intent = EditIntent.Paste };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
    }
}
