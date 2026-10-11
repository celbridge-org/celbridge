using Celbridge.Automation;
using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Projects.Commands;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests for the event the load project command reports when a load fails.
/// </summary>
[TestFixture]
public class LoadProjectCommandTests
{
    private const string ProjectFile = "/projects/sample/sample.celbridge";

    private IProjectLoader _projectLoader = null!;
    private IAutomationService _automationService = null!;
    private LoadProjectCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectLoader = Substitute.For<IProjectLoader>();
        _automationService = Substitute.For<IAutomationService>();

        _command = new LoadProjectCommand(
            Substitute.For<ICommandService>(),
            Substitute.For<IProjectService>(),
            _projectLoader,
            _automationService)
        {
            ProjectFilePath = ProjectFile
        };
    }

    [Test]
    public async Task ALoadThatFails_ReportsTheFailedLoad()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns(Task.FromResult<Result>(Result.Fail("The load failed")));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFile));
    }

    [Test]
    public async Task ALoaderThatThrows_FailsTheLoadAndReportsIt()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns<Task<Result>>(_ => throw new InvalidOperationException("The loader threw"));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFile));
    }

    [Test]
    public async Task ALoadThatSucceeds_ReportsNoFailure()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns(Task.FromResult<Result>(Result.Ok()));

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }
}
