using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class UndoEntityCommand : CommandBase, IUndoEntityCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public UndoEntityCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var applyResult = entityService.UndoPatch(Resource);

        await Task.CompletedTask;

        return applyResult;
    }

    //
    // Static methods for scripting support.
    //

    public static void UndoEntity(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUndoEntityCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
