using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.Services.Commands;
using Celbridge.Services.Project;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectDataTests
{
    private IProjectDataService? _projectDataService;
    private ICommandService? _commandService;

    private string? _projectFolder;

    [SetUp]
    public void Setup()
    {
        _projectFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Celbridge/{nameof(ProjectDataTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }

        _commandService = new CommandService();
        _projectDataService = new ProjectDataService(_commandService);

        _commandService.StartExecutingCommands();
    }

    [TearDown]
    public void TearDown()
    {
        Guard.IsNotNull(_commandService);
        _commandService.StopExecutingCommands();

        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder!, true);
        }
    }

    [Test]
    public async Task ICanCreateAndLoadProjectDataAsync()
    {
        Guard.IsNotNull(_commandService);
        Guard.IsNotNull(_projectDataService);
        Guard.IsNotNullOrEmpty(_projectFolder);

        var createResult = await _projectDataService.CreateProjectDataAsync(_projectFolder, "TestProjectA");
        createResult.IsSuccess.Should().BeTrue();

        var projectPath = createResult.Value;

        var loadResult = _projectDataService.LoadProjectData(projectPath);
        loadResult.IsSuccess.Should().BeTrue();

        var projectData = _projectDataService.LoadedProjectData!;
        projectData.Should().NotBeNull();
        projectData.ProjectName.Should().Be("TestProjectA");

        var unloadProjectDataCommand = new UnloadProjectDataCommand(_projectDataService);
        _commandService.ExecuteCommand(unloadProjectDataCommand);
        
        for (int i = 0; i < 10; i++)
        {
            if (_projectDataService.LoadedProjectData is null)
            {
                break;
            }
            await Task.Delay(10);
        }

        _projectDataService.LoadedProjectData.Should().BeNull();

        Directory.Delete(_projectFolder, true);
        File.Exists(projectPath).Should().BeFalse();
    }
}
