using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Newtonsoft.Json.Linq;

namespace Celbridge.ProjectAdmin.Services;

public class ProjectDataService : IProjectDataService
{
    private readonly IUtilityService _utilityService;

    private const string ProjectDataFileKey = "projectDataFile";
    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";

    public ProjectDataService(IUtilityService utilityService)
    {
        _utilityService = utilityService;
    }

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

        if (!_utilityService.IsPathSegmentValid(config.ProjectName))
        {
            return Result.Fail($"Project name is not valid: {config.ProjectName}");
        }

        return Result.Ok();
    }

    public async Task<Result> CreateProjectDataAsync(NewProjectConfig config)
    {
        // Todo: Check that the config is valid

        try
        {
            // Todo: Create the data files in a temp directory and moved them into place if all operations succeed

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
                    "{{ProjectDataFileKey}}": "{{DefaultProjectDataPath}}",
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

            var createResult = await ProjectData.CreateProjectDataAsync(config.ProjectFilePath, databasePath);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project database: {config.ProjectName}");
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

            string projectDataPathRelative = jsonObject["projectDataFile"]!.ToString();
            string projectDataPath = Path.Combine(projectFolder, projectDataPathRelative);

            var loadResult = ProjectData.LoadProjectData(projectPath, projectDataPath);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load project database: {projectDataPath}");
            }

            // Both data files have successfully loaded, so we can now populate the member variables
            LoadedProjectData = loadResult.Value;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project database. {ex.Message}");
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
