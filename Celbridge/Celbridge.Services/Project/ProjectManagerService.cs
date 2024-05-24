using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using LiteDB;

namespace Celbridge.Services.Project;

public class ProjectManagerService : IProjectManagerService
{
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;

    public ProjectManagerService(
        ILoggingService loggingService,
        INavigationService navigationService)
    {
        _loggingService = loggingService;
        _navigationService = navigationService;
    }

    public Result CreateProject(string folder, string projectName)
    {
        try
        {
            // Save empty project db file inside a folder named after the project
            var projectFolder = Path.Combine(folder, projectName);
            Directory.CreateDirectory(projectFolder);

            var dbPath = Path.Combine(projectFolder, $"{projectName}.celbridge");
            using (var db = new LiteDatabase(dbPath))
            {
                var col = db.GetCollection<ProjectConfig>("ProjectConfig");

                var projectConfig = new ProjectConfig
                {
                    Version = 1
                };

                col.Insert(projectConfig);
            }

            _loggingService.Info($"Created project: {dbPath}");
        }
        catch (Exception ex)
        {
            _loggingService.Error($"Failed to create project: {projectName}. {ex.Message}");
            return Result.Fail(ex.Message);
        }

        return Result.Ok();
    }

    public Result OpenProject(string projectPath)
    {
        /*
        try
        {
            // Check the project file is valid
            // Todo: Handle version upgrades here
            using (var db = new LiteDatabase(projectPath))
            {
                var col = db.GetCollection<ProjectConfig>("ProjectConfig");
                var projectConfig = col.FindAll().FirstOrDefault();

                if (projectConfig == null)
                {
                    _loggingService.Error($"Project config not found in: {projectPath}");
                    return Result.Fail($"Project config not found in: {projectPath}");
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.Error($"Failed to open project: {projectPath}. {ex.Message}");
            return Result.Fail(ex.Message);
        }
        */

        _navigationService.NavigateToPage("WorkspacePage");

        return Result.Ok();
    }

}
