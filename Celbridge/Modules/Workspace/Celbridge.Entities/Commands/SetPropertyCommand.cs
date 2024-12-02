using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class SetPropertyCommand : CommandBase, ISetPropertyCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public int ComponentIndex { get; set; }
    public string PropertyPath { get; set; } = string.Empty;
    public string JsonValue { get; set; } = string.Empty;

    public SetPropertyCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var applyResult = entityService.SetProperty(Resource, ComponentIndex, PropertyPath, JsonValue);

        await Task.CompletedTask;

        return applyResult;
    }

    //
    // Static methods for scripting support.
    //

    public static void SetProperty(ResourceKey resource, int componentIndex, string propertyPath, string jsonValue)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISetPropertyCommand>(command =>
        {
            command.Resource = resource;
            command.ComponentIndex = componentIndex;
            command.PropertyPath = propertyPath;
            command.JsonValue = jsonValue;
        });
    }
}
