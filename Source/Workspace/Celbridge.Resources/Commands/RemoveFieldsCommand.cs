using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Atomically removes a batch of fields through the sidecar data service.
/// </summary>
public sealed class RemoveFieldsCommand : CommandBase, IRemoveFieldsCommand
{
    // RemoveFieldsAsync never creates or deletes the sidecar file (an empty
    // sidecar is kept after the last field is removed), so the registry never
    // needs to learn about a new file. CommandFlags stays None — the existing
    // sidecar's classification cannot change as a result of a content update.
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey Resource { get; set; }
    public IReadOnlyList<string> Names { get; set; } = Array.Empty<string>();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveFieldsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Names is null
            || Names.Count == 0)
        {
            return Result.Fail("Names list must contain at least one entry.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var removeResult = await sidecarService.RemoveFieldsAsync(Resource, Names);
        if (removeResult.IsFailure)
        {
            return Result.Fail(removeResult);
        }
        return Result.Ok();
    }
}
