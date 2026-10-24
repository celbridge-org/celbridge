using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically writes a batch of fields through the sidecar data service.
/// </summary>
public sealed class SetFieldsCommand : CommandBase, ISetFieldsCommand
{
    public ResourceKey Resource { get; set; }
    public IReadOnlyDictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SetFieldsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Fields is null
            || Fields.Count == 0)
        {
            return Result.Fail("Fields dictionary must contain at least one entry.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var setResult = await sidecarService.SetFieldsAsync(Resource, Fields);
        if (setResult.IsFailure)
        {
            return Result.Fail(setResult);
        }

        return Result.Ok();
    }
}
