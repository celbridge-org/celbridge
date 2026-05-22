using System.Text;
using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Returns the resource's full sidecar frontmatter plus the ordered list of
/// block descriptors in one call.
/// </summary>
public sealed class GetInfoCommand : CommandBase, IGetInfoCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }

    public GetInfoResult ResultValue { get; private set; } = new GetInfoResult(
        new Dictionary<string, object>(),
        Array.Empty<SidecarBlockDescriptor>());

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public GetInfoCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
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
        var fields = new Dictionary<string, object>(content.Frontmatter, StringComparer.Ordinal);
        var blocks = content.Blocks
            .Select(b => new SidecarBlockDescriptor(b.Name, Encoding.UTF8.GetByteCount(b.Content)))
            .ToList();

        ResultValue = new GetInfoResult(fields, blocks);
        return Result.Ok();
    }
}
