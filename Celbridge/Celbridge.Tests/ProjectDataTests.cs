using Celbridge.BaseLibrary.Project;
using Celbridge.Services.Project;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectDataTests
{
    private IProjectDataService? _projectDataService;

    private string? _projectFolder;

    [SetUp]
    public void Setup()
    {
        _projectFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Celbridge/{nameof(ProjectDataTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }

        _projectDataService = new ProjectDataService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder!, true);
        }
    }

    [Test]
    public async Task ICanCreateAndLoadProjectDataAsync()
    {
        Guard.IsNotNull(_projectDataService);
        Guard.IsNotNullOrEmpty(_projectFolder);

        var newProjectConfig = new NewProjectConfig("TestProjectA", _projectFolder);
        var projectFilePath = newProjectConfig.ProjectFilePath;

        var createResult = await _projectDataService.CreateProjectDataAsync(newProjectConfig);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _projectDataService.LoadProjectData(projectFilePath);
        loadResult.IsSuccess.Should().BeTrue();

        var projectData = _projectDataService.LoadedProjectData!;
        projectData.Should().NotBeNull();
        projectData.ProjectName.Should().Be("TestProjectA");

        _projectDataService.UnloadProjectData();
        _projectDataService.LoadedProjectData.Should().BeNull();

        Directory.Delete(_projectFolder, true);
        File.Exists(projectFilePath).Should().BeFalse();
    }
}
