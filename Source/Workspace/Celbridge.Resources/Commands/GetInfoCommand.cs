using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Returns the resource's full sidecar field set in one call.
/// </summary>
public sealed class GetInfoCommand : CommandBase, IGetInfoCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }

    public GetInfoResult ResultValue { get; private set; } = new GetInfoResult(
        new Dictionary<string, object>(),
        HasSidecar: false);

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public GetInfoCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var readResult = await sidecarService.ReadAsync(Resource);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        if (read.Outcome == SidecarReadOutcome.NoSidecar)
        {
            // Empty result already set on ResultValue; signal success so callers
            // can iterate uniformly across "no sidecar" and "has sidecar".
            return Result.Ok();
        }

        if (read.Outcome == SidecarReadOutcome.Broken)
        {
            return Result.Fail($"Sidecar for resource '{Resource}' is broken: {read.FailureMessage}. Use file_read for raw inspection or data_check_project for the system-level view.");
        }

        var content = read.Content!;
        var fields = new Dictionary<string, object>(content.Fields, StringComparer.Ordinal);

        ResultValue = new GetInfoResult(fields, HasSidecar: true);
        return Result.Ok();
    }
}
