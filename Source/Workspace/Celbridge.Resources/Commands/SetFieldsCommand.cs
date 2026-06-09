using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically writes a batch of fields through the sidecar data service.
/// </summary>
public sealed class SetFieldsCommand : CommandBase, ISetFieldsCommand
{
    // CommandFlags.UpdateResources triggers a synchronous project-tree rescan
    // after the command runs, which is needed only when a new sidecar file
    // appears on disk. For in-place updates of an existing sidecar the file is
    // already in the registry and its classification does not change, so the
    // rescan is wasted work. The flag is computed from the outcome set by
    // ExecuteAsync.
    public override CommandFlags CommandFlags =>
        _outcome == SidecarWriteOutcome.Created ? CommandFlags.UpdateResources : CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public IReadOnlyDictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();

    private SidecarWriteOutcome _outcome = SidecarWriteOutcome.NoChange;

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

        _outcome = setResult.Value;
        return Result.Ok();
    }
}
