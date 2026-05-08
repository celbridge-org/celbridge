using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_get_info for file resources.
/// </summary>
public record class FileInfoResult(string Type, long Size, string Modified, string Extension, bool IsText, int? LineCount);

/// <summary>
/// Result returned by file_get_info for folder resources.
/// </summary>
public record class FolderInfoResult(string Type, string Modified);

public partial class FileTools
{
    /// <summary>
    /// Returns resource metadata: type, size, modified date, extension, text/binary indicator, and line count for text files.
    /// </summary>
    /// <param name="resource">Resource key to inspect.</param>
    /// <returns>JSON object with type, plus size/extension/isText/lineCount for files (modified is always set).</returns>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public async partial Task<CallToolResult> GetInfo(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command resolves the registry
        // path and captures disk metadata on the command thread.
        var getInfoResult = await ExecuteCommandAsync<IGetFileInfoCommand, FileInfoSnapshot>(
            command => command.Resource = resourceKey);
        if (getInfoResult.IsFailure)
        {
            return ToolError(getInfoResult);
        }
        var snapshot = getInfoResult.Value;

        if (!snapshot.Exists)
        {
            return ToolError($"Resource not found: '{resource}'");
        }

        if (snapshot.IsFile)
        {
            var fileResult = new FileInfoResult(
                "file",
                snapshot.Size,
                snapshot.ModifiedUtc.ToString("o"),
                snapshot.Extension,
                snapshot.IsText,
                snapshot.LineCount);
            return ToolSuccess(SerializeJson(fileResult));
        }

        var folderResult = new FolderInfoResult("folder", snapshot.ModifiedUtc.ToString("o"));
        return ToolSuccess(SerializeJson(folderResult));
    }
}
