using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Removes a named content block from the parent resource's .cel sidecar.
/// No-op when the block or the sidecar is absent.
/// </summary>
public sealed class RemoveBlockCommand : CommandBase, IRemoveBlockCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string BlockId { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveBlockCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        return await sidecarService.RemoveBlockAsync(Resource, BlockId);
    }
}
