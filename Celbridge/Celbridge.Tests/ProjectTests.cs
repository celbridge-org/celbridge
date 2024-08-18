using Celbridge.Projects.Services;
using CommunityToolkit.Diagnostics;
using Celbridge.Projects;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectTests
{
    private IProjectService? _projectService;

    private string? _projectFolderPath;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ProjectTests)}");
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, true);
        }

        _projectService = new ProjectService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath!, true);
        }
    }

    [Test]
    public async Task ICanCreateAndLoadProjectAsync()
    {
        Guard.IsNotNull(_projectService);
        Guard.IsNotNullOrEmpty(_projectFolderPath);

        var newProjectConfig = new NewProjectConfig("TestProjectA", _projectFolderPath);
        var projectFilePath = newProjectConfig.ProjectFilePath;

        var createResult = await _projectService.CreateProjectAsync(newProjectConfig);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _projectService.LoadProject(projectFilePath);
        loadResult.IsSuccess.Should().BeTrue();

        //
        // ProjectData tests
        //

        var project = _projectService.LoadedProject!;
        project.Should().NotBeNull();
        project.ProjectName.Should().Be("TestProjectA");

        var versionResultA = await project.GetDataVersionAsync();
        versionResultA.IsSuccess.Should().BeTrue();

        //
        // Unload the project database
        //

        _projectService.UnloadProject();
        _projectService.LoadedProject.Should().BeNull();

        //
        // Delete the project database files
        //

        Directory.Delete(_projectFolderPath, true);
        File.Exists(projectFilePath).Should().BeFalse();
    }

    [Test]
    public void ICanValidateANewProjectConfig()
    {
        Guard.IsNotNull(_projectService);
        Guard.IsNotNullOrEmpty(_projectFolderPath);

        { 
            var config = new NewProjectConfig("TestProjectA", _projectFolderPath);
            var result = _projectService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeTrue();
        }

        {
            var config = new NewProjectConfig("Test/ProjectA", _projectFolderPath);
            var result = _projectService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeFalse();
        }

        {
            var config = new NewProjectConfig("TestProjectA", string.Empty);
            var result = _projectService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeFalse();
        }
    }
}
