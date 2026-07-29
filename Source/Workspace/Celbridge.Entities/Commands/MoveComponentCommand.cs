using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class MoveComponentCommand : CommandBase, IMoveComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public int SourceComponentIndex { get; set; }
    public int DestComponentIndex { get; set; }

    public MoveComponentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var copyResult = entityService.MoveComponent(Resource, SourceComponentIndex, DestComponentIndex);
        if (copyResult.IsFailure)
        {
            return Result.Fail($"Failed to move entity component form index '{SourceComponentIndex}' to index '{DestComponentIndex}': '{Resource}'.")
                .WithErrors(copyResult);
        }

        await Task.CompletedTask;

        return copyResult;
    }
}
