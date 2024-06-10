using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Newtonsoft.Json.Linq;

namespace Celbridge.Services.Project;

public class ProjectDataService : IProjectDataService
{
    private const int ProjectVersion = 1;

    private readonly INavigationService _navigationService;
    private readonly IUserInterfaceService _userInterfaceService;

    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";

    public ProjectDataService(
        INavigationService navigationService,
        IUserInterfaceService userInterfaceService)
    {
        _navigationService = navigationService;
        _userInterfaceService = userInterfaceService;
    }

    public IProjectData? LoadedProjectData { get; private set; }

    public async Task<Result<string>> CreateProjectDataAsync(string folder, string projectName)
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

            var projectPath = Path.Combine(projectFolder, $"{projectName}.celbridge");
            var projectJson = $$"""
                {
                    "projectDataFile": "{{DefaultProjectDataPath}}"
                }
                """;

            await File.WriteAllTextAsync(projectPath, projectJson);

            //
            // Create a database file inside a folder named after the project
            //

            var databasePath = Path.Combine(projectFolder, DefaultProjectDataPath);
            string? dataFolder = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            var createResult = await ProjectData.CreateProjectDataAsync(projectPath, databasePath, ProjectVersion);
            if (createResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to create project: {projectName}");
            }

            return Result<string>.Ok(projectPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to create project: {projectName}. {ex.Message}");
        }
    }

    public Result OpenProjectData(string projectPath)
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

            var loadResult = ProjectData.LoadProjectData(projectPath, databasePath);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load project data: {databasePath}");
            }

            LoadedProjectData = loadResult.Value;

            _navigationService.NavigateToPage("WorkspacePage");
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project. {ex.Message}");
        }
    }

    public Result CloseProjectData()
    {
        if (LoadedProjectData is null)
        {
            // Closing a project that is not open is a no-op
            return Result.Ok();
        }

        var disposableProjectData = LoadedProjectData as IDisposable;
        Guard.IsNotNull(disposableProjectData);

        disposableProjectData.Dispose();

        LoadedProjectData = null;

        _navigationService.NavigateToPage("StartPage");

        return Result.Ok();
    }
}
