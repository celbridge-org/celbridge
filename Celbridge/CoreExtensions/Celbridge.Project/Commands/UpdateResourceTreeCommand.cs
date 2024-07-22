using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Projects.Commands;

public class UpdateResourceTreeCommand : CommandBase, IUpdateResourceTreeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UpdateResourceTreeCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to execute {nameof(UpdateResourceTreeCommand)} because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;

        var updateResult = resourceRegistry.UpdateResourceTree();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to execute {nameof(UpdateResourceTreeCommand)}. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void UpdateResourceTree()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUpdateResourceTreeCommand>();
    }
}
