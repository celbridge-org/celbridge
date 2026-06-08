using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Writes a single field through the sidecar data service.
/// </summary>
public sealed class SetFieldCommand : CommandBase, ISetFieldCommand
{
    // CommandFlags.UpdateResources triggers a synchronous project-tree rescan
    // after the command runs, which is needed only when a new sidecar file
    // appears on disk. For in-place updates of an existing sidecar the file is
    // already in the registry and its classification does not change, so the
    // rescan is wasted work — typically the dominant cost of a setField call.
    // The flag is computed from the outcome set by ExecuteAsync.
    public override CommandFlags CommandFlags =>
        _outcome == SidecarWriteOutcome.Created ? CommandFlags.UpdateResources : CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public string Field { get; set; } = string.Empty;
    public object? Value { get; set; }

    private SidecarWriteOutcome _outcome = SidecarWriteOutcome.NoChange;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SetFieldCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Value is null)
        {
            return Result.Fail("Value is null.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var setResult = await sidecarService.SetFieldAsync(Resource, Field, Value);
        if (setResult.IsFailure)
        {
            return Result.Fail(setResult);
        }

        _outcome = setResult.Value;
        return Result.Ok();
    }
}
