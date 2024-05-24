using Celbridge.BaseLibrary.Project;

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
            File.WriteAllText(dbPath, "<project>");

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
