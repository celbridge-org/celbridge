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
    /// <summary>Get metadata for a single file or folder resource.</summary>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public async partial Task<CallToolResult> GetInfo(string resource)
    {
        const string ToolGuide = "file_get_info";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command resolves the registry
        // path and captures disk metadata on the command thread.
        var getInfoResult = await ExecuteCommandAsync<IGetFileInfoCommand, FileInfoSnapshot>(
            command => command.Resource = resourceKey);
        if (getInfoResult.IsFailure)
        {
            return ToolResponse.Error(getInfoResult, ToolGuide);
        }
        var snapshot = getInfoResult.Value;

        if (!snapshot.Exists)
        {
            return ToolResponse.ResourceNotFound(resource, ToolGuide);
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
            return ToolResponse.Success(SerializeJson(fileResult));
        }

        var folderResult = new FolderInfoResult("folder", snapshot.ModifiedUtc.ToString("o"));
        return ToolResponse.Success(SerializeJson(folderResult));
    }
}
