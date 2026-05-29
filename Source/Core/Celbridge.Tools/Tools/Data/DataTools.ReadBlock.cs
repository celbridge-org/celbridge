using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Read a named content block from a resource's .cel sidecar.</summary>
    [McpServerTool(Name = "data_read_block", ReadOnly = true)]
    [ToolAlias("data.read_block")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> ReadBlock(string resource, string blockId)
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

        var commandResult = await ExecuteCommandAsync<IReadBlockCommand, string>(command =>
        {
            command.Resource = resourceKey;
            command.BlockId = blockId;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success(commandResult.Value);
    }
}
