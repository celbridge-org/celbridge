using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Enumerates every paired-sidecar parent resource whose .cel frontmatter
/// "tags" list contains the given tag value.
/// </summary>
public sealed class FindTagCommand : CommandBase, IFindTagCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public string Tag { get; set; } = string.Empty;

    public IReadOnlyList<ResourceKey> ResultValue { get; private set; } = Array.Empty<ResourceKey>();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public FindTagCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Tag))
        {
            return Result.Fail("Tag must be a non-empty string.");
        }

        var scanner = _workspaceWrapper.WorkspaceService.ResourceScanner;
        ResultValue = await scanner.FindByTagAsync(Tag);
        return Result.Ok();
    }
}
