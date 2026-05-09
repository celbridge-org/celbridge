using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Open the interactive rename dialog for a resource (user picks the new name).</summary>
    [McpServerTool(Name = "explorer_rename")]
    [ToolAlias("explorer.rename")]
    public async partial Task<CallToolResult> Rename(string resource)
    {
        const string ToolGuide = "explorer_rename";

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
            return ToolResponse.Error(renameResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
