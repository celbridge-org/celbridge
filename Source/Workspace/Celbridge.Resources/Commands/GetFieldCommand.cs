using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Reads a single frontmatter field through the sidecar data service.
/// SuppressCommandLog because reads should not clutter the command log.
/// </summary>
public sealed class GetFieldCommand : CommandBase, IGetFieldCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }
    public string Field { get; set; } = string.Empty;

    public object ResultValue { get; private set; } = new object();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public GetFieldCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Field))
        {
            return Result.Fail("Field must be a non-empty string.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        var readResult = await sidecarService.ReadAsync(Resource);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        if (read.Outcome == SidecarReadOutcome.NoSidecar)
        {
            return Result.Fail($"Resource '{Resource}' has no sidecar.");
        }
        if (read.Outcome == SidecarReadOutcome.Broken)
        {
            return Result.Fail($"Sidecar for resource '{Resource}' is broken: {read.FailureMessage}");
        }

        if (!read.Content!.Frontmatter.TryGetValue(Field, out var value))
        {
            return Result.Fail(
                $"Field '{Field}' is not set on resource '{Resource}'. " +
                "Use data_get_info to see the fields currently set on this resource.");
        }

        ResultValue = value;
        return Result.Ok();
    }
}
