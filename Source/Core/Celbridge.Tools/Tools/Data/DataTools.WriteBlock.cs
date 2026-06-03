using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Write a named content block in a resource's .cel sidecar (creates the sidecar if missing; overwrites existing block of the same name).</summary>
    [McpServerTool(Name = "data_write_block", Idempotent = true)]
    [ToolAlias("data.write_block")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> WriteBlock(string resource, string blockId, string content)
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
        var sidecarService = GetRequiredService<IWorkspaceWrapper>().WorkspaceService.ResourceService.Sidecars;
        if (!sidecarService.IsValidBlockName(blockId))
        {
            return ToolResponse.Error($"block_id '{blockId}' does not match the block-naming rules (lowercase letters, digits, hyphens, dotted segments).");
        }
        content ??= string.Empty;

        var commandResult = await ExecuteCommandAsync<IWriteBlockCommand>(command =>
        {
            command.Resource = resourceKey;
            command.BlockId = blockId;
            command.Content = content;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
