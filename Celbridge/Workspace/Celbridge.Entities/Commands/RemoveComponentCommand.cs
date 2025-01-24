using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class RemoveComponentCommand : CommandBase, IRemoveComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentKey ComponentKey { get; set; } = ComponentKey.Empty;

    public RemoveComponentCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var removeResult = entityService.RemoveComponent(ComponentKey);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to remove entity component: '{ComponentKey}'")
                .WithErrors(removeResult);
        }

        await Task.CompletedTask;

        return removeResult;
    }

    //
    // Static methods for scripting support.
    //

    public static async Task<Result> RemoveComponent(ResourceKey resource, int componentIndex)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();

        return await commandService.ExecuteAsync<IRemoveComponentCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(resource, componentIndex);
        });
    }
}
