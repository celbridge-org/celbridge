using System.Text.Json;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for file and folder operations: read-only queries plus content writes
/// (write, apply edits, find/replace, delete lines, write binary).
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

    private static Result<List<TextEdit>> ParseEditsJson(string editsJson)
    {
        var edits = new List<TextEdit>();
        var jsonDocument = JsonDocument.Parse(editsJson);

        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail("Edits JSON must be an array of edit objects");
        }

        int index = 0;
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("line", out var lineElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'line'");
            }

            var column = element.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : 1;

            if (!element.TryGetProperty("endLine", out var endLineElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'endLine'");
            }

            var endColumn = element.TryGetProperty("endColumn", out var endColumnElement) ? endColumnElement.GetInt32() : -1;

            if (!element.TryGetProperty("newText", out var newTextElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'newText'");
            }

            var line = lineElement.GetInt32();
            var endLine = endLineElement.GetInt32();
            var newText = newTextElement.GetString() ?? string.Empty;

            edits.Add(new TextEdit(line, column, endLine, endColumn, newText));
            index++;
        }

        return edits;
    }
}
