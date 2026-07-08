using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Server;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers ProjectUnloader's workspace page cleanup wait: the successful unload, the early-out when no
/// project is loaded, and the bounded timeout that fails the unload instead of blocking the command queue
/// when the workspace page never unloads.
/// </summary>
[TestFixture]
public class ProjectUnloaderTests
{
    private IProjectService _projectService = null!;
    private INavigationService _navigationService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IServerService _serverService = null!;
    private ProjectUnloader _projectUnloader = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _navigationService = Substitute.For<INavigationService>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _serverService = Substitute.For<IServerService>();

        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("TestProject");
        _projectService.CurrentProject.Returns(project);

        _navigationService.NavigateToPage(Arg.Any<string>()).Returns(Result.Ok());

        _projectUnloader = new ProjectUnloader(
            new NullLogger<ProjectUnloader>(),
            _projectService,
            _navigationService,
            _workspaceWrapper,
            _serverService);
    }

    [Test]
    public async Task UnloadProject_WithNoProjectLoaded_Succeeds()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task UnloadProject_WhenWorkspacePageUnloads_Succeeds()
    {
        // The workspace page reports loaded until the second poll, then unloads.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true, true, false);

        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsSuccess, Is.True);
        _projectService.Received(1).ClearCurrentProject();
        await _serverService.Received(1).StopAsync();
    }

    [Test]
    public async Task UnloadProject_WhenWorkspacePageNeverUnloads_FailsAfterTimeout()
    {
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _projectUnloader.UnloadTimeoutMs = 200;

        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsFailure, Is.True);
        _projectService.DidNotReceive().ClearCurrentProject();
    }
}
