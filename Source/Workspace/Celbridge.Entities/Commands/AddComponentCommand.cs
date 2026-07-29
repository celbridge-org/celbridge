using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class AddComponentCommand : CommandBase, IAddComponentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentKey ComponentKey { get; set; } = ComponentKey.Empty;
    public string ComponentType { get; set; } = string.Empty;

    public AddComponentCommand(IWorkspaceWrapper workspaceWrapper)
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
}
