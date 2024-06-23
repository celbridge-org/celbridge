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

        //
        // ProjectData tests
        //

        var projectData = _projectDataService.LoadedProjectData!;
        projectData.Should().NotBeNull();
        projectData.ProjectName.Should().Be("TestProjectA");

        var versionResultA = await projectData.GetDataVersionAsync();
        versionResultA.IsSuccess.Should().BeTrue();

        //
        // WorkspaceData tests
        //

        var workspaceData = _projectDataService.WorkspaceData!;
        workspaceData.Should().NotBeNull();
        var versionResultB = await workspaceData.GetDataVersionAsync();
        versionResultB.IsSuccess.Should().BeTrue();

        // 
        // Set and get an expanded folders list in the project user data
        //
        var expandedFolders = new List<string>() { "a", "b", "c" };
        var setFoldersResult = await workspaceData.SetExpandedFoldersAsync(expandedFolders);
        setFoldersResult.IsSuccess.Should().BeTrue();

        var getFoldersResult = await workspaceData.GetExpandedFoldersAsync();
        getFoldersResult.IsSuccess.Should().BeTrue();

        expandedFolders.SequenceEqual(getFoldersResult.Value);

        //
        // Check the project data and project user data versions match
        //
        versionResultA.Value.Should().Be(versionResultB.Value);

        //
        // Unload the project databases
        //

        _projectDataService.UnloadProjectData();
        _projectDataService.LoadedProjectData.Should().BeNull();
        _projectDataService.WorkspaceData.Should().BeNull();

        //
        // Delete the project database files
        //

        Directory.Delete(_projectFolder, true);
        File.Exists(projectFilePath).Should().BeFalse();
    }

    [Test]
    public void ICanValidateANewProjectConfig()
    {
        Guard.IsNotNull(_projectDataService);
        Guard.IsNotNullOrEmpty(_projectFolder);

        { 
            var config = new NewProjectConfig("TestProjectA", _projectFolder);
            var result = _projectDataService.ValidateNewProjectConfig(config);
            result.IsSuccess.Should().BeTrue();
        }

        {
            var config = new NewProjectConfig("Test/ProjectA", _projectFolder);
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
