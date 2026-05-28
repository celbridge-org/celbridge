using System.Text.Json;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for file and folder operations: read-only queries plus content writes
/// (write, edit, multi-edit, replace, write binary).
/// </summary>
[McpServerToolType]
public partial class FileTools : AgentToolBase
{
    public FileTools(IApplicationServiceProvider services) : base(services) { }

    private static void CollectFolderResources(IFolderResource folder, IResourceRegistry registry, List<ResourceKey> folderKeys)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFolderResource childFolder)
            {
                var childKey = registry.GetResourceKey(child);
                folderKeys.Add(childKey);
                CollectFolderResources(childFolder, registry, folderKeys);
            }
        }
    }

    private static bool IsTextFile(ITextBinarySniffer textBinarySniffer, string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (textBinarySniffer.IsBinaryExtension(extension))
        {
            return false;
        }

        var result = textBinarySniffer.IsTextFile(filePath);
        return result.IsSuccess && result.Value;
    }

    private static string GetMimeType(string extension)
    {
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".tar" => "application/x-tar",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            _ => "application/octet-stream"
        };
    }

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Reads the post-edit file content as lines so an edit tool can attach a
    /// small surrounding-context window to each affected range. Returns null
    /// when the resource cannot be resolved or the file no longer exists, so
    /// the caller can fall back to ranges without context.
    /// </summary>
    private static async Task<string[]?> ReadFileLinesForContextAsync(IResourceFileSystem fileSystem, ResourceKey fileResourceKey)
    {
        var infoResult = await fileSystem.GetInfoAsync(fileResourceKey);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != ResourceInfoKind.File)
        {
            return null;
        }

        var readResult = await fileSystem.ReadAllTextAsync(fileResourceKey);
        if (readResult.IsFailure)
        {
            return null;
        }

        return LineEndingHelper.SplitToContentLines(readResult.Value).ToArray();
    }

    /// <summary>
    /// Returns the affected lines plus one surrounding line on each side as a
    /// contextLines window, or null when no file content is available. Uses
    /// 1-based inclusive line numbers to match the range types in the
    /// Foundation result records.
    /// </summary>
    private static List<string>? BuildContextLines(string[]? fileLines, int fromLine, int toLine)
    {
        if (fileLines is null)
        {
            return null;
        }

        var contextStartIndex = Math.Max(0, fromLine - 2);
        var contextEndIndex = Math.Min(fileLines.Length - 1, toLine);

        return fileLines
            .Skip(contextStartIndex)
            .Take(contextEndIndex - contextStartIndex + 1)
            .ToList();
    }
}
