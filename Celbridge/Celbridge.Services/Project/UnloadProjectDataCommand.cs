using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.Project;

public class UnloadProjectDataCommand : CommandBase
{
    private IProjectDataService _projectDataService;

    public UnloadProjectDataCommand(IProjectDataService projectDataService)
    {
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (_projectDataService.LoadedProjectData is null)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        await Task.CompletedTask;

        return _projectDataService.UnloadProjectData();
    }
}
