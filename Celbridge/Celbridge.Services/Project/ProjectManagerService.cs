using Celbridge.BaseLibrary.Project;
using LiteDB;

namespace Celbridge.Services.Project;

public class ProjectManagerService : IProjectManagerService
{
    private readonly ILoggingService _loggingService;

    public ProjectManagerService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
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
}
