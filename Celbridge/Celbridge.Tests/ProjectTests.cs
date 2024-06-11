using Celbridge.BaseLibrary.Project;
using Celbridge.Services.Project;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectTests
{
    private IProjectDataService? _projectDataService;

    [SetUp]
    public void Setup()
    {
        _projectDataService = new ProjectDataService();
    }

    [TearDown]
    public void TearDown()
    {}

    [Test]
    public async Task ICanCreateAndLoadProjectDataAsync()
    {
        Guard.IsNotNull(_projectDataService);

        var projectFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestProject");

        var createResult = await _projectDataService.CreateProjectDataAsync(projectFolder, "TestA");
        createResult.IsSuccess.Should().BeTrue();

        var projectPath = createResult.Value;

        var loadResult = _projectDataService.LoadProjectData(projectPath);
        loadResult.IsSuccess.Should().BeTrue();

        var projectData = _projectDataService.LoadedProjectData!;
        projectData.Should().NotBeNull();
        projectData.ProjectName.Should().Be("TestA");

        _projectDataService.UnloadProjectData();
        _projectDataService.LoadedProjectData.Should().BeNull();

        Directory.Delete(projectFolder, true);
        File.Exists(projectPath).Should().BeFalse();
    }
}
