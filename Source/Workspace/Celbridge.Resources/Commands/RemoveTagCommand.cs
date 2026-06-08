using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Removes a tag from the parent resource's .cel sidecar tags list. Idempotent.
/// </summary>
public sealed class RemoveTagCommand : CommandBase, IRemoveTagCommand
{
    // RemoveTagAsync never creates or deletes the sidecar file (an empty
    // sidecar is kept after the last tag is removed), so the registry never
    // needs to learn about a new file. CommandFlags stays None — the existing
    // sidecar's classification cannot change as a result of a content update.
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public string Tag { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveTagCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var removeResult = await sidecarService.RemoveTagAsync(Resource, Tag);
        if (removeResult.IsFailure)
        {
            return Result.Fail(removeResult);
        }
        return Result.Ok();
    }
}
