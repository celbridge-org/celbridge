using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Enumerates every distinct tag string across the workspace's healthy
/// sidecar files via the scanner. Backs the data_list_tags MCP tool.
/// </summary>
public sealed class ListTagsCommand : CommandBase, IListTagsCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public IReadOnlyList<string> ResultValue { get; private set; } = Array.Empty<string>();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ListTagsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var scanner = _workspaceWrapper.WorkspaceService.ResourceService.Scanner;
        ResultValue = await scanner.ListAllTagsAsync();
        return Result.Ok();
    }
}
