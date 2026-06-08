using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Appends a tag to the parent resource's .cel sidecar tags list, creating
/// the sidecar if missing. Idempotent.
/// </summary>
public sealed class AddTagCommand : CommandBase, IAddTagCommand
{
    // CommandFlags.UpdateResources triggers a synchronous project-tree rescan
    // after the command runs, which is needed only when a new sidecar file
    // appears on disk. For tag appends to an existing sidecar the file is
    // already in the registry and its classification does not change, so the
    // rescan is wasted work. The flag is computed from the outcome set by
    // ExecuteAsync.
    public override CommandFlags CommandFlags =>
        _outcome == SidecarWriteOutcome.Created ? CommandFlags.UpdateResources : CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public string Tag { get; set; } = string.Empty;

    private SidecarWriteOutcome _outcome = SidecarWriteOutcome.NoChange;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public AddTagCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var addResult = await sidecarService.AddTagAsync(Resource, Tag);
        if (addResult.IsFailure)
        {
            return Result.Fail(addResult);
        }

        _outcome = addResult.Value;
        return Result.Ok();
    }
}
