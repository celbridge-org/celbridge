using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Newtonsoft.Json.Linq;

namespace Celbridge.Services.Project;

public class ProjectAdminService : IProjectAdminService
{
    private const int ProjectVersion = 1;

    private readonly INavigationService _navigationService;

    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";

    public ProjectAdminService(
        INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public IProjectData? ProjectData { get; private set; }

    public async Task<Result<string>> CreateProjectAsync(string folder, string projectName)
    {
        try
        {
            var projectFolder = Path.Combine(folder, projectName);

            if (Directory.Exists(projectFolder))
            {
                return Result<string>.Fail($"Project folder already exists: {projectFolder}");
            }

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

            var createResult = await Project.ProjectData.CreateProjectDataAsync(projectName, databasePath, ProjectVersion);
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

    public Result<IProjectData> LoadProjectData(string projectName, string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectName);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        try
        {
            var loadResult = Project.ProjectData.LoadProjectData(projectName, databasePath);
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

    public Result OpenProjectWorkspace(string projectName, string projectPath)
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

            var loadResult = LoadProjectData(projectName, databasePath);
            if (loadResult.IsFailure)
            {
                return loadResult;
            }

            ProjectData = loadResult.Value;

            _navigationService.NavigateToPage("WorkspacePage", string.Empty);
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project. {ex.Message}");
        }
    }

    public Result CloseProjectWorkspace()
    {
        throw new NotImplementedException();
    }
}
