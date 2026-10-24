using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically removes a batch of tags from the parent resource's .cel sidecar
/// tag list. Idempotent.
/// </summary>
public sealed class RemoveTagsCommand : CommandBase, IRemoveTagsCommand
{
    public ResourceKey Resource { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

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

        return Result.Ok();
    }
}
