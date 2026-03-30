using System.Text.Json;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for read-only file and folder queries.
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
}
