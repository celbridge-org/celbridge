using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Removes a single field through the sidecar data service.
/// </summary>
public sealed class RemoveFieldCommand : CommandBase, IRemoveFieldCommand
{
    // RemoveFieldAsync never creates or deletes the sidecar file (an empty
    // sidecar is kept after the last field is removed), so the registry never
    // needs to learn about a new file. CommandFlags stays None — the existing
    // sidecar's classification cannot change as a result of a content update.
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public string Field { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveFieldCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var removeResult = await sidecarService.RemoveFieldAsync(Resource, Field);
        if (removeResult.IsFailure)
        {
            return Result.Fail(removeResult);
        }
        return Result.Ok();
    }
}
