using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class AddComponentCommand : CommandBase, IAddComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentKey ComponentKey { get; set; } = ComponentKey.Empty;
    public string ComponentType { get; set; } = string.Empty;

    public AddComponentCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var addResult = entityService.AddComponent(ComponentKey, ComponentType);
        if (addResult.IsFailure)
        {
            return Result.Fail($"Failed to add component of type '{ComponentType}' to entity: '{ComponentKey}'")
                .WithErrors(addResult);
        }

        await Task.CompletedTask;

        return addResult;
    }

    //
    // Static methods for scripting support.
    //

    public static async Task<Result> AddComponent(ResourceKey resource, int insertAtIndex, string componentType)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        var result = await commandService.ExecuteAsync<IAddComponentCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(resource, insertAtIndex);
            command.ComponentType = componentType;
        });

        return result;
    }
}
