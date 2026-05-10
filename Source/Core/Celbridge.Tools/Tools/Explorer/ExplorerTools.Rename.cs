using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Open the interactive rename dialog for a resource (user picks the new name).</summary>
    [McpServerTool(Name = "explorer_rename")]
    [ToolAlias("explorer.rename")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Rename(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var renameResult = await ExecuteCommandAsync<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        if (renameResult.IsFailure)
        {
            return ToolResponse.Error(renameResult);
        }

        return ToolResponse.Success("ok");
    }
}
