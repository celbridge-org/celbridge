using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class FileTools
{
    /// <summary>Toggle the filesystem read-only attribute on a file or folder.</summary>
    [McpServerTool(Name = "file_set_writeable", Idempotent = true)]
    [ToolAlias("file.set_writeable")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> SetWriteable(string fileResource, bool writeable)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        var commandResult = await ExecuteCommandAsync<ISetWriteableCommand>(command =>
        {
            command.Resource = fileResourceKey;
            command.Writeable = writeable;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
