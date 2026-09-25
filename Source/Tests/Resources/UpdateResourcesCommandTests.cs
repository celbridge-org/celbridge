using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class UpdateResourcesCommandTests
{
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void SetUp()
    {
        _resourceService = Substitute.For<IResourceService>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesTheRescan_ByDefault()
    {
        var command = new UpdateResourcesCommand(_workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _resourceService.Received(1).ScheduleResourceUpdate();
        await _resourceService.DidNotReceive().UpdateResourcesAsync();
    }

    [Test]
    public async Task ExecuteAsync_RescansBeforeCompleting_WhenImmediate()
    {
        _resourceService.UpdateResourcesAsync().Returns(Result.Ok());

        var command = new UpdateResourcesCommand(_workspaceWrapper) { Immediate = true };
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _resourceService.Received(1).UpdateResourcesAsync();
        _resourceService.DidNotReceive().ScheduleResourceUpdate();
    }

    [Test]
    public async Task ExecuteAsync_ReportsAFailedRescan_WhenImmediate()
    {
        _resourceService.UpdateResourcesAsync().Returns(Result.Fail("Failed to update resources"));

        var command = new UpdateResourcesCommand(_workspaceWrapper) { Immediate = true };
        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
