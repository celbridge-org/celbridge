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
    /// Gets metadata about a resource including type, size, modified date, extension, and text/binary indicator.
    /// For text files, also returns the line count.
    /// </summary>
    /// <param name="resource">Resource key of the resource to inspect.</param>
    /// <returns>JSON object with fields: type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601), extension (string, files only), isText (bool, files only), lineCount (int, text files only).</returns>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public async partial Task<CallToolResult> GetInfo(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command resolves the registry
        // path and captures disk metadata on the command thread.
        var (callResult, snapshot) = await ExecuteCommandAsync<IGetFileInfoCommand, FileInfoSnapshot>(
            command => command.Resource = resourceKey);

        if (callResult.IsError == true || snapshot is null)
        {
            return callResult;
        }

        if (!snapshot.Exists)
        {
            return ErrorResult($"Resource not found: '{resource}'");
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
            return SuccessResult(SerializeJson(fileResult));
        }

        var folderResult = new FolderInfoResult("folder", snapshot.ModifiedUtc.ToString("o"));
        return SuccessResult(SerializeJson(folderResult));
    }
}
