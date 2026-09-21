using Celbridge.Resources;
using Celbridge.UserInterface.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Verifies that MoveDownloadCommand moves a staged download through the resource gateway, and that it
/// refuses any source outside temp:, so it cannot be used to take a file out of the project.
/// </summary>
[TestFixture]
public class MoveDownloadCommandTests
{
    private static readonly ResourceKey StagedResource = new("temp:downloads/0hggcxmu.pdf");
    private static readonly ResourceKey Destination = new("downloads/report.pdf");

    private IResourceFileSystem _resourceFileSystem = null!;
    private MoveDownloadCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem.MoveAsync(Arg.Any<ResourceKey>(), Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<MoveResult>.Ok(new MoveResult(
                Array.Empty<ResourceKey>(),
                Array.Empty<SkippedReferencer>(),
                SidecarOutcome.NotPresent))));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _command = new MoveDownloadCommand(workspaceWrapper);
    }

    [Test]
    public async Task AStagedDownload_IsMovedThroughTheGateway()
    {
        _command.SourceResource = StagedResource;
        _command.DestResource = Destination;

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        await _resourceFileSystem.Received(1).MoveAsync(StagedResource, Destination);
    }

    [Test]
    public async Task ASourceOutsideTemp_IsRefused_AndNothingMoves()
    {
        _command.SourceResource = new ResourceKey("notes.md");
        _command.DestResource = Destination;

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().MoveAsync(Arg.Any<ResourceKey>(), Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task AMoveTheGatewayRefuses_IsReportedAsAFailure()
    {
        _resourceFileSystem.MoveAsync(StagedResource, Destination)
            .Returns(Task.FromResult(Result<MoveResult>.Fail("Destination already exists")));

        _command.SourceResource = StagedResource;
        _command.DestResource = Destination;

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
