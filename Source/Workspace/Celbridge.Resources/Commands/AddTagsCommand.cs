using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically appends a batch of tags to the parent resource's .cel sidecar
/// tag list, creating the sidecar if missing. Idempotent.
/// </summary>
public sealed class AddTagsCommand : CommandBase, IAddTagsCommand
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
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    private SidecarWriteOutcome _outcome = SidecarWriteOutcome.NoChange;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public AddTagsCommand(IWorkspaceWrapper workspaceWrapper)
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
        var addResult = await sidecarService.AddTagsAsync(Resource, Tags);
        if (addResult.IsFailure)
        {
            return Result.Fail(addResult);
        }

        _outcome = addResult.Value;
        return Result.Ok();
    }
}
