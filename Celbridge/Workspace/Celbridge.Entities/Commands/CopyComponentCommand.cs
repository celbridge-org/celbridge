using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class CopyComponentCommand : CommandBase, ICopyComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public int SourceComponentIndex { get; set; }
    public int DestComponentIndex { get; set; }

    public CopyComponentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var copyResult = entityService.CopyComponent(Resource, SourceComponentIndex, DestComponentIndex);
        if (copyResult.IsFailure)
        {
            return Result.Fail($"Failed to move entity component form index '{SourceComponentIndex}' to index '{DestComponentIndex}': '{Resource}'.")
                .WithErrors(copyResult);
        }

        await Task.CompletedTask;

        return copyResult;
    }

    //
    // Static methods for scripting support.
    //

    public static async Task<Result> CopyComponent(ResourceKey resource, int sourceComponentIndex, int destComponentIndex)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        return await commandService.ExecuteAsync<ICopyComponentCommand>(command =>
        {
            command.Resource = resource;
            command.SourceComponentIndex = sourceComponentIndex;
            command.DestComponentIndex = destComponentIndex;
        });
    }
}
