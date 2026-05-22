using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Remove a named content block from a resource's .cel sidecar (no-op if absent).</summary>
    [McpServerTool(Name = "data_remove_block", Destructive = true, Idempotent = true)]
    [ToolAlias("data.remove_block")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> RemoveBlock(string resource, string blockId)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        var sidecarError = ValidateNotSidecarKey(resourceKey, resource);
        if (sidecarError is not null)
        {
            return sidecarError;
        }
        var sidecarService = GetRequiredService<IWorkspaceWrapper>().WorkspaceService.SidecarService;
        if (!sidecarService.IsValidBlockName(blockId))
        {
            return ToolResponse.Error($"block_id '{blockId}' does not match the block-naming rules (lowercase letters, digits, hyphens, dotted segments).");
        }

        var commandResult = await ExecuteCommandAsync<IRemoveBlockCommand>(command =>
        {
            command.Resource = resourceKey;
            command.BlockId = blockId;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
