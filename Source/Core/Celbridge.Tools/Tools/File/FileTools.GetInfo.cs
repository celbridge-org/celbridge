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
    public partial CallToolResult GetInfo(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (File.Exists(resourcePath))
        {
            var fileInfo = new FileInfo(resourcePath);
            var textBinarySniffer = GetRequiredService<ITextBinarySniffer>();
            var isText = IsTextFile(textBinarySniffer, resourcePath);
            int? lineCount = null;

            if (isText)
            {
                lineCount = File.ReadAllLines(resourcePath).Length;
            }

            var result = new FileInfoResult(
                "file",
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.ToString("o"),
                fileInfo.Extension,
                isText,
                lineCount);
            return SuccessResult(SerializeJson(result));
        }

        if (Directory.Exists(resourcePath))
        {
            var directoryInfo = new DirectoryInfo(resourcePath);
            var result = new FolderInfoResult("folder", directoryInfo.LastWriteTimeUtc.ToString("o"));
            return SuccessResult(SerializeJson(result));
        }

        return ErrorResult($"Resource not found: '{resource}'");
    }
}
