using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Writes a single frontmatter field through the sidecar data service.
/// Sets CommandFlags.UpdateResources so the registry refreshes after the
/// write, making the new sidecar visible to subsequent reads (data_find_tag,
/// data_check_project, the rename cascade).
/// </summary>
public sealed class SetFieldCommand : CommandBase, ISetFieldCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string Field { get; set; } = string.Empty;
    public object? Value { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SetFieldCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Value is null)
        {
            return Result.Fail("Value is null.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.SidecarService;
        return await sidecarService.SetFieldAsync(Resource, Field, Value);
    }
}
