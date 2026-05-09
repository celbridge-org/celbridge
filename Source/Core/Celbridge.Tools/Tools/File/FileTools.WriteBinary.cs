using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class FileTools
{
    /// <summary>Wholesale-replace a binary file from base64-encoded bytes, creating it if missing.</summary>
    [McpServerTool(Name = "file_write_binary")]
    [ToolAlias("file.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        var writeResult = await ExecuteCommandAsync<IWriteBinaryFileCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
        });
        if (writeResult.IsFailure)
        {
            return ToolResponse.Error(writeResult);
        }

        return ToolResponse.Success("ok");
    }
}
