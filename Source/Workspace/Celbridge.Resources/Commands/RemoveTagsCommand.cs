using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically removes a batch of tags from the parent resource's .cel sidecar
/// tag list. Idempotent.
/// </summary>
public sealed class RemoveTagsCommand : CommandBase, IRemoveTagsCommand
{
    // CommandFlags.UpdateResources triggers a synchronous project-tree rescan
    // after the command runs, needed only when removing the last tag empties
    // the sidecar and the now-blank file is deleted from disk. In-place content
    // updates leave the file in the registry with an unchanged classification,
    // so the rescan is skipped. The flag is computed from the outcome set by
    // ExecuteAsync.
    public override CommandFlags CommandFlags =>
        _outcome == SidecarWriteOutcome.Deleted ? CommandFlags.UpdateResources : CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    private SidecarWriteOutcome _outcome = SidecarWriteOutcome.NoChange;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveTagsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Tags is null
            || Tags.Count == 0)
        {
            return Result.Fail("Tags list must contain at least one entry.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var removeResult = await sidecarService.RemoveTagsAsync(Resource, Tags);
        if (removeResult.IsFailure)
        {
            return Result.Fail(removeResult);
        }

        _outcome = removeResult.Value;
        return Result.Ok();
    }
}
