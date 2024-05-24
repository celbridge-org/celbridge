using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.Project;

public class ProjectManagerService : IProjectManagerService
{
    private readonly ILoggingService _loggingService;

    public ProjectManagerService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void CreateProject(string projectName)
    {
        _loggingService.Info($"Creating project: {projectName}");
    }
}
