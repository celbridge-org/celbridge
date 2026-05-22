using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Removes a single frontmatter field through the sidecar data service.
/// </summary>
public sealed class RemoveFieldCommand : CommandBase, IRemoveFieldCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string Field { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveFieldCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        return await sidecarService.MutateFrontmatterAsync(
            Resource,
            dict => dict.Remove(Field),
            createIfMissing: false);
    }
}
