using Celbridge.BaseLibrary.Commands.Workspace;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Commands.Workspace;

public class SaveWorkspaceStateCommand : CommandBase, ISaveWorkspaceStateCommand
{
    private readonly IProjectDataService _projectDataService;

    public SaveWorkspaceStateCommand(
        IProjectDataService projectDataService)
    {
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Todo: Get the list of expanded folders from the Resource Tree View
        // Todo: Set the expanded folders to the ProjectUserData
        // Todo: Get the selected resource from the Resource Tree View
        // Todo: Set the selected resource in the ProjectUserData

        return Result.Ok();
    }
}
