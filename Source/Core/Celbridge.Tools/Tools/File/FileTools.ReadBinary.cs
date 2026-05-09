using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_read_binary with base64-encoded file content.
/// </summary>
public record class FileReadBinaryResult(string Base64, string MimeType, int Size);

public partial class FileTools
{
    /// <summary>Read any file as base64-encoded bytes plus MIME type. Use file_read_image for inline images.</summary>
    [McpServerTool(Name = "file_read_binary", ReadOnly = true)]
    [ToolAlias("file.read_binary")]
    public async partial Task<CallToolResult> ReadBinary(string resource)
    {
        const string ToolGuide = "file_read_binary";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to resolve path for resource: '{resource}'", ToolGuide);
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ToolResponse.Error($"File not found: '{resource}'", ToolGuide);
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        var result = new FileReadBinaryResult(base64, mimeType, bytes.Length);
        return ToolResponse.Success(SerializeJson(result));
    }
}
