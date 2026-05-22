using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Creates or overwrites a named content block in the parent resource's
/// .cel sidecar.
/// </summary>
public sealed class WriteBlockCommand : CommandBase, IWriteBlockCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WriteBlockCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var blockId = BlockId;
        var content = Content;
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        return await sidecarService.MutateBlocksAsync(
            Resource,
            blocks =>
            {
                var index = -1;
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (string.Equals(blocks[i].Name, blockId, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                var updated = new SidecarBlock(blockId, content);
                if (index >= 0)
                {
                    blocks[index] = updated;
                }
                else
                {
                    blocks.Add(updated);
                }
            });
    }
}
