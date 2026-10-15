using Celbridge.Projects;
using Celbridge.Projects.Commands;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests that the load project command hands its project file and origin to the loader, and refuses an empty path.
/// </summary>
[TestFixture]
public class LoadProjectCommandTests
{
    private const string ProjectFile = "/projects/sample/sample.celbridge";

    private IProjectLoader _projectLoader = null!;

    [SetUp]
    public void Setup()
    {
        _projectLoader = Substitute.For<IProjectLoader>();
        _projectLoader.LoadProjectAsync(default!, default).ReturnsForAnyArgs(Result.Ok());
    }

    [Test]
    public async Task TheCommand_PassesTheProjectFileAndOriginToTheLoader()
    {
        var command = new LoadProjectCommand(_projectLoader)
        {
            ProjectFilePath = ProjectFile,
            Origin = ProjectLoadOrigin.LaunchFile
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _projectLoader.Received(1).LoadProjectAsync(ProjectFile, ProjectLoadOrigin.LaunchFile);
    }

    [Test]
    public async Task AnEmptyPath_FailsWithoutLoading()
    {
        var command = new LoadProjectCommand(_projectLoader);

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _projectLoader.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default);
    }
}
