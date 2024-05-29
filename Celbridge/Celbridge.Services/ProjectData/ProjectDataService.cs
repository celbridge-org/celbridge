using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Newtonsoft.Json.Linq;

namespace Celbridge.Services.ProjectData;

public class ProjectDataService : IProjectDataService
{
    private readonly INavigationService _navigationService;

    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";

    public ProjectDataService(
        INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public async Task<Result<string>> CreateProjectDataAsync(string folder, string projectName, int version)
    {
        try
        {
            var projectFolder = Path.Combine(folder, projectName);
            Directory.CreateDirectory(projectFolder);

            //
            // Write the .celbridge Json file in the project folder
            //

            var projectJsonPath = Path.Combine(projectFolder, $"{projectName}.celbridge");
            var projectJsonData = $$"""
                {
                    "projectDataFile": "{{DefaultProjectDataPath}}"
                }
                """;

            File.WriteAllText(projectJsonPath, projectJsonData);

            //
            // Create a database file inside a folder named after the project
            //

            var databasePath = Path.Combine(projectFolder, DefaultProjectDataPath);
            string? dataFolder = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            var createResult = await ProjectData.CreateProjectDataAsync(databasePath, 1);
            if (createResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to create project: {projectName}");
            }

            return Result<string>.Ok(projectJsonPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to create project: {projectName}. {ex.Message}");
        }
    }

    public Result<IProjectData> LoadProjectData(string databasePath)
    {
        try
        {
            var loadResult = ProjectData.LoadProjectData(databasePath);
            if (loadResult.IsFailure)
            {
                return Result<IProjectData>.Fail($"Failed to load project data: {databasePath}");
            }

            var projectData = loadResult.Value;

            return Result<IProjectData>.Ok(projectData);
        }
        catch (Exception ex)
        {
            return Result<IProjectData>.Fail($"Failed to load project data: {databasePath}. {ex.Message}");
        }
    }

    public Result OpenProjectWorkspace(string projectPath)
    {
        // Load the project first, then open the workspace because projects may use more than
        // one editor UI in future.

        try
        {
            var projectJsonData = File.ReadAllText(projectPath);
            var jsonObject = JObject.Parse(projectJsonData);
            Guard.IsNotNull(jsonObject);

            var projectFolder = Path.GetDirectoryName(projectPath)!; 
            string relativePath = jsonObject["projectDataFile"]!.ToString();
            string databasePath = Path.Combine(projectFolder, relativePath);

            var loadResult = LoadProjectData(databasePath);
            if (loadResult.IsFailure)
            {
                return loadResult;
            }

            var projectData = loadResult.Value;

            _navigationService.NavigateToPage("WorkspacePage", projectData);
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project. {ex.Message}");
        }

    }

}
