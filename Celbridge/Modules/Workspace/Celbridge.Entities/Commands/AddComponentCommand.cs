using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class AddComponentCommand : CommandBase, IAddComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public int ComponentIndex { get; set; }

    public AddComponentCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var addResult = entityService.AddComponent(Resource, ComponentType, ComponentIndex);
        if (addResult.IsFailure)
        {
            return Result.Fail($"Failed to add component of type '{ComponentType}' to entity for resource '{Resource}' at index '{ComponentIndex}'.")
                .WithErrors(addResult);
        }

        await Task.CompletedTask;

        return addResult;
    }

    public static void AddComponent(ResourceKey resource, string componentType, int insertAtIndex)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();

        commandService.Execute<IAddComponentCommand>(command =>
        {
            command.Resource = resource;
            command.ComponentType = componentType;
            command.ComponentIndex = insertAtIndex;
        });
    }
}
