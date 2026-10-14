using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Projects.Commands;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests for the command that opens the current project's data folder in the file manager.
/// </summary>
[TestFixture]
public class OpenProjectDataCommandTests
{
    private const string DataFolderPath = "/data/Projects/sample-0123456789abcdef";

    private IProjectService _projectService = null!;
    private IFileManagerLauncher _fileManagerLauncher = null!;
    private OpenProjectDataCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _fileManagerLauncher = Substitute.For<IFileManagerLauncher>();
        _fileManagerLauncher.OpenFileManagerAsync(Arg.Any<string>()).Returns(Task.FromResult<Result>(Result.Ok()));

        _command = new OpenProjectDataCommand(_projectService, _fileManagerLauncher);
    }

    [Test]
    public async Task ALoadedProject_OpensItsDataFolder()
    {
        var project = Substitute.For<IProject>();
        project.ProjectDataFolderPath.Returns(DataFolderPath);
        _projectService.CurrentProject.Returns(project);

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _fileManagerLauncher.Received(1).OpenFileManagerAsync(DataFolderPath);
    }

    [Test]
    public async Task NoLoadedProject_FailsAndOpensNothing()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _fileManagerLauncher.DidNotReceive().OpenFileManagerAsync(Arg.Any<string>());
    }

    [Test]
    public async Task AFileManagerThatFails_FailsTheCommand()
    {
        var project = Substitute.For<IProject>();
        project.ProjectDataFolderPath.Returns(DataFolderPath);
        _projectService.CurrentProject.Returns(project);
        _fileManagerLauncher.OpenFileManagerAsync(DataFolderPath).Returns(Task.FromResult<Result>(Result.Fail("The launch failed")));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
