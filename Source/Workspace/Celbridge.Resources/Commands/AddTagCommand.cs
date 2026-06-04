using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Appends a tag to the parent resource's .cel sidecar tags list, creating
/// the sidecar if missing. Idempotent.
/// </summary>
public sealed class AddTagCommand : CommandBase, IAddTagCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string Tag { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public AddTagCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        return await sidecarService.AddTagAsync(Resource, Tag);
    }
}
