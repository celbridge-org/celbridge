using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class FileTools
{
    /// <summary>
    /// Replaces the content of a binary file from base64-encoded data.
    /// </summary>
    /// <param name="fileResource">Resource key of the file.</param>
    /// <param name="base64Content">New content as base64.</param>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "file_write_binary")]
    [ToolAlias("file.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolError($"Invalid resource key: '{fileResource}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteBinaryFileCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
        });
        if (writeResult.IsFailure)
        {
            return ToolError(writeResult);
        }

        return ToolSuccess("ok");
    }
}
