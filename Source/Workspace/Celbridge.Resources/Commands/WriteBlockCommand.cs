using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Creates or overwrites a named content block in the parent resource's
/// .cel sidecar.
/// </summary>
public sealed class WriteBlockCommand : CommandBase, IWriteBlockCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WriteBlockCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        return await sidecarService.WriteBlockAsync(Resource, BlockId, Content);
    }
}
