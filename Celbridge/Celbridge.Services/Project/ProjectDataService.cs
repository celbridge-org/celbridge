using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Newtonsoft.Json.Linq;

namespace Celbridge.Services.Project;

public class ProjectDataService : IProjectDataService, ICommandExecutor
{
    private const int ProjectVersion = 1;

    private const string DefaultProjectDataPath = "Library/ProjectData/ProjectData.db";

    private readonly ICommandService _commandService;

    public ProjectDataService(ICommandService commandService)
    {
        _commandService = commandService;

        // No need to unregister because this service has application lifetime
        _commandService.RegisterExecutor(this); 
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

    public bool CanExecuteCommand(CommandBase command)
    {
        return command is UnloadProjectDataCommand;
    }

    public async Task<Result> ExecuteCommand(CommandBase command)
    {
        if (command is UnloadProjectDataCommand unloadCommand)
        {
            return await unloadCommand.ExecuteAsync();
        }
        return Result.Fail($"Unknown command type {command}");
    }
}
