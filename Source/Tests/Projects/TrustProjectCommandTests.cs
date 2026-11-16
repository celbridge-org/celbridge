using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Projects.Commands;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests for the command behind the Trust Project button, which records the trust and then reloads the project.
/// </summary>
[TestFixture]
public class TrustProjectCommandTests
{
    private const string ProjectFilePath = "/projects/Acme/Acme.celbridge";

    private IProjectService _projectService = null!;
    private ICommandService _commandService = null!;
    private TrustProjectCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _projectService.TrustProjectAsync(Arg.Any<string>()).Returns(Result.Ok());
        _commandService = Substitute.For<ICommandService>();

        _command = new TrustProjectCommand(_commandService, _projectService);
    }

    [Test]
    public async Task ALoadedProject_IsTrustedThenReloaded()
    {
        StubCurrentProject();

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _projectService.TrustProjectAsync(ProjectFilePath);
            _commandService.Execute(Arg.Any<Action<IReloadProjectCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
        });
    }

    [Test]
    public async Task ATrustThatCantBeRecorded_FailsWithoutAReload()
    {
        StubCurrentProject();
        _projectService.TrustProjectAsync(ProjectFilePath).Returns(Result.Fail("The data folder is read-only"));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _commandService.DidNotReceive().Execute(
            Arg.Any<Action<IReloadProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task NoLoadedProject_Fails()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _projectService.DidNotReceive().TrustProjectAsync(Arg.Any<string>());
        _commandService.DidNotReceive().Execute(
            Arg.Any<Action<IReloadProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    private void StubCurrentProject()
    {
        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(ProjectFilePath);
        _projectService.CurrentProject.Returns(project);
    }
}
