using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Projects.Commands;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests for the command that unloads the open project and loads the same project file again.
/// </summary>
[TestFixture]
public class ReloadProjectCommandTests
{
    private IProjectService _projectService = null!;
    private ICommandService _commandService = null!;
    private ReloadProjectCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _commandService = Substitute.For<ICommandService>();

        _command = new ReloadProjectCommand(_commandService, _projectService);
    }

    [Test]
    public async Task NoLoadedProject_Fails()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _commandService.DidNotReceive().ExecuteImmediate(
            Arg.Any<Action<IUnloadProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
        _commandService.DidNotReceive().Execute(
            Arg.Any<Action<ILoadProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }
}
