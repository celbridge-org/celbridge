using Celbridge.Services.ProjectData;
using Uno.Disposables;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectTests
{
    [SetUp]
    public void Setup()
    {}

    [TearDown]
    public void TearDown()
    {}

    [Test]
    public async Task ICanCreateAndLoadAProjectAsync()
    {
        var folder = System.IO.Path.GetTempPath();
        var projectDatabaseName = "ProjectData.db";
        var projectPath = System.IO.Path.Combine(folder, projectDatabaseName);
        var projectName = "TestProject";

        var createResult = await ProjectData.CreateProjectDataAsync(projectName, projectPath, 1);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = ProjectData.LoadProjectData(projectName, projectPath);
        loadResult.IsSuccess.Should().BeTrue();
        var projectData = loadResult.Value;
        
        var config = await projectData.GetConfigAsync();
        config.Version.Should().Be(1);

        projectData.TryDispose();

        File.Delete(projectPath);
        File.Exists(projectPath).Should().BeFalse();
    }
}
