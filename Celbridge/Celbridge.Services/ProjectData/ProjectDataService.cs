using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;

namespace Celbridge.Services.ProjectData;

public class ProjectDataService : IProjectDataService
{
    private readonly INavigationService _navigationService;

    public ProjectDataService(
        INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public Result<IProjectData> CreateProjectData(string folder, string projectName, int version)
    {
        try
        {
            // Save a new project file inside a folder named after the project
            var projectFolder = Path.Combine(folder, projectName);
            Directory.CreateDirectory(projectFolder);

            var databasePath = Path.Combine(projectFolder, $"{projectName}.celbridge");
            var config = new ProjectConfig(Version: version);
            var createResult = ProjectData.CreateProjectData(databasePath, config);
            if (createResult.IsFailure)
            {
                return Result<IProjectData>.Fail($"Failed to create project: {projectName}");
            }

            var projectData = createResult.Value;

            return Result<IProjectData>.Ok(projectData);
        }
        catch (Exception ex)
        {
            return Result<IProjectData>.Fail($"Failed to create project: {projectName}. {ex.Message}");
        }
    }

    public Result<IProjectData> LoadProjectData(string projectPath)
    {
        try
        {
            var loadResult = ProjectData.LoadProjectData(projectPath);
            if (loadResult.IsFailure)
            {
                return Result<IProjectData>.Fail($"Failed to load project: {projectPath}");
            }

            var projectData = loadResult.Value;

            return Result<IProjectData>.Ok(projectData);
        }
        catch (Exception ex)
        {
            return Result<IProjectData>.Fail($"Failed to load project: {projectPath}. {ex.Message}");
        }
    }

    public Result OpenProjectWorkspace(string projectPath)
    {
        // Load the project first, then open the workspace because projects may use more than
        // one editor UI in future.

        var loadResult = LoadProjectData(projectPath);
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        var projectData = loadResult.Value;

        _navigationService.NavigateToPage("WorkspacePage", projectData);

        return Result.Ok();
    }

}
