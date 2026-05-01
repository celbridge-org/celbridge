using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class FileTools
{
    /// <summary>
    /// Replaces the content of a binary file from base64-encoded data.
    /// Decoded bytes are written directly to disk. Any open document reloads
    /// its buffer from disk after the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write.</param>
    /// <param name="base64Content">The new content as a base64-encoded string.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "file_write_binary")]
    [ToolAlias("file.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IWriteBinaryFileCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
        });
    }
}
