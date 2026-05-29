using Celbridge.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Reads a named content block through the sidecar data service.
/// </summary>
public sealed class ReadBlockCommand : CommandBase, IReadBlockCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }
    public string BlockId { get; set; } = string.Empty;

    public string ResultValue { get; private set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ReadBlockCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!SidecarHelper.IsValidBlockName(BlockId))
        {
            return Result.Fail($"block_id '{BlockId}' does not match the block-naming rules.");
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

        var block = read.Content!.Blocks.FirstOrDefault(b => string.Equals(b.Name, BlockId, StringComparison.Ordinal));
        if (block is null)
        {
            return Result.Fail($"Block '{BlockId}' is not present on resource '{Resource}'.");
        }

        ResultValue = block.Content;
        return Result.Ok();
    }
}
