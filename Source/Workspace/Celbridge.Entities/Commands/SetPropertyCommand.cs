using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Entities.Commands;

public class SetPropertyCommand : CommandBase, ISetPropertyCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentKey ComponentKey { get; set; } = ComponentKey.Empty;
    public string PropertyPath { get; set; } = string.Empty;
    public string JsonValue { get; set; } = string.Empty;
    public bool Insert { get; set; }

    public SetPropertyCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var setResult = entityService.SetProperty(ComponentKey, PropertyPath, JsonValue, Insert);

        await Task.CompletedTask;

        return setResult;
    }
}
