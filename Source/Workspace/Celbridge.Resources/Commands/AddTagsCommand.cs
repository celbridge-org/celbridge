using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically appends a batch of tags to the parent resource's .cel sidecar
/// tag list, creating the sidecar if missing. Idempotent.
/// </summary>
public sealed class AddTagsCommand : CommandBase, IAddTagsCommand
{
    public ResourceKey Resource { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

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

        return Result.Ok();
    }
}
