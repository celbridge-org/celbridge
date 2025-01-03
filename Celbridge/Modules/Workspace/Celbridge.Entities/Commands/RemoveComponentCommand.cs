using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class RemoveComponentCommand : CommandBase, IRemoveComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public int ComponentIndex { get; set; }

    public RemoveComponentCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var removeResult = entityService.RemoveComponent(Resource, ComponentIndex);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to remove entity component at index '{ComponentIndex}' from resource '{Resource}'.")
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
            command.Resource = resource;
            command.ComponentIndex = componentIndex;
        });
    }
}
