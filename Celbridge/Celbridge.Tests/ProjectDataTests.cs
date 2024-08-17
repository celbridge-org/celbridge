using Celbridge.ProjectAdmin.Services;
using CommunityToolkit.Diagnostics;
using Celbridge.Projects;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectDataTests
{
    private IProjectDataService? _projectDataService;

    private string? _projectFolderPath;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ProjectDataTests)}");
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, true);
        }

        _projectDataService = new ProjectDataService();
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
    public async Task ICanCreateAndLoadProjectDataAsync()
    {
        Guard.IsNotNull(_projectDataService);
        Guard.IsNotNullOrEmpty(_projectFolderPath);

        var newProjectConfig = new NewProjectConfig("TestProjectA", _projectFolderPath);
        var projectFilePath = newProjectConfig.ProjectFilePath;

        var createResult = await _projectDataService.CreateProjectAsync(newProjectConfig);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _projectDataService.LoadProject(projectFilePath);
        loadResult.IsSuccess.Should().BeTrue();

        //
        // ProjectData tests
        //

        var project = _projectDataService.LoadedProject!;
        project.Should().NotBeNull();
        project.ProjectName.Should().Be("TestProjectA");

        var versionResultA = await project.GetDataVersionAsync();
        versionResultA.IsSuccess.Should().BeTrue();

        //
        // Unload the project database
        //

        _projectDataService.UnloadProject();
        _projectDataService.LoadedProject.Should().BeNull();

        //
        // Delete the project database files
        //

        Directory.Delete(_projectFolderPath, true);
        File.Exists(projectFilePath).Should().BeFalse();
    }

    [Test]
    public void ICanValidateANewProjectConfig()
    {
        Guard.IsNotNull(_projectDataService);
        Guard.IsNotNullOrEmpty(_projectFolderPath);

        { 
            var config = new NewProjectConfig("TestProjectA", _projectFolderPath);
            var result = _projectDataService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeTrue();
        }

        {
            var config = new NewProjectConfig("Test/ProjectA", _projectFolderPath);
            var result = _projectDataService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeFalse();
        }

        {
            var config = new NewProjectConfig("TestProjectA", string.Empty);
            var result = _projectDataService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeFalse();
        }
    }
}
