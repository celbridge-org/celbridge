using Celbridge.BaseLibrary.Project;
using Newtonsoft.Json.Linq;

namespace Celbridge.Services.Project;

public class ProjectDataService : IProjectDataService
{
    private const int ProjectDataVersion = 1;
    private const int ProjectUserDataVersion = 1;

    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";
    private const string DefaultProjectUserDataPath = "Library/ProjectData/ProjectUserData.db";

    public ProjectDataService()
    {}

    public IProjectData? LoadedProjectData { get; private set; }

    public Result ValidateNewProjectConfig(NewProjectConfig config)
    {
        if (config is null)
        {
            return Result.Fail("New project config is null.");
        }

        if (string.IsNullOrWhiteSpace(config.Folder))
        {
            return Result.Fail("Project folder is empty.");
        }

        return ValidateProjectName(config.ProjectName);
    }

    public Result ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return Result.Fail("Project name is empty.");
        }

        // It's pretty much impossible to robustly check for invalid characters in a path in a way that
        // works on all platforms. As a "best effort" solution, we do a basic check for invalid characters.
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in projectName)
        {
            if (invalidChars.Contains(c))
            {
                return Result.Fail($"Project name '{projectName}' contains invalid characters.");
            }
        }

        return Result.Ok();
    }

    public async Task<Result> CreateProjectDataAsync(NewProjectConfig config)
    {
        // Todo: Check that the config is valid

        try
        {
            if (Directory.Exists(config.ProjectFolder))
            {
                return Result<string>.Fail($"Project folder already exists: {config.ProjectFolder}");
            }

            Directory.CreateDirectory(config.ProjectFolder);

            //
            // Write the .celbridge Json file in the project folder
            //

            var projectJson = $$"""
                {
                    "projectDataFile": "{{DefaultProjectDataPath}}"
                }
                """;

            await File.WriteAllTextAsync(config.ProjectFilePath, projectJson);

            //
            // Create a database file inside a folder named after the project
            //

            var databasePath = Path.Combine(config.ProjectFolder, DefaultProjectDataPath);
            string? dataFolder = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            var createResult = await ProjectData.CreateProjectDataAsync(config.ProjectFilePath, databasePath, ProjectDataVersion);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project: {config.ProjectName}");
            }

            //
            // Create a user data database file beside the project database
            //

            var userDatabasePath = Path.Combine(config.ProjectFolder, DefaultProjectUserDataPath);            
            var createUserResult = await ProjectUserData.CreateProjectUserDataAsync(userDatabasePath, ProjectUserDataVersion);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project user data: {config.ProjectName}");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create project: {config.ProjectName}. {ex.Message}");
        }
    }

    public Result LoadProjectData(string projectPath)
    {
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
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project. {ex.Message}");
        }
    }

    public Result UnloadProjectData()
    {
        if (LoadedProjectData is null)
        {
            // Unloading a project that is not loaded is a no-op
            return Result.Ok();
        }

        var disposableProjectData = LoadedProjectData as IDisposable;
        Guard.IsNotNull(disposableProjectData);

        disposableProjectData.Dispose();

        LoadedProjectData = null;

        return Result.Ok();
    }
}
