using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// JSON metadata returned alongside the image content block from file_read_image.
/// </summary>
public record class FileReadImageResult(string Resource, string MimeType, int SizeBytes);

public partial class FileTools
{
    // Soft cap on the inline image payload to avoid saturating the agent's context.
    private const long MaxInlineImageBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> SupportedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    /// <summary>Read a JPEG/PNG/GIF/WebP image as an inline multimodal content block for viewing.</summary>
    [McpServerTool(Name = "file_read_image", ReadOnly = true)]
    [ToolAlias("file.read_image")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> ReadImage(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to resolve path for resource: '{resourceKey}'");
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ToolResponse.Error($"File not found: '{resourceKey}'");
        }

        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        if (!SupportedImageMimeTypes.TryGetValue(extension, out var mimeType))
        {
            return ToolResponse.Error(
                $"file_read_image does not support extension '{extension}'. " +
                $"Supported formats: .jpg, .jpeg, .png, .gif, .webp. " +
                $"For other binary content, use file_read_binary.");
        }

        var fileInfo = new FileInfo(resourcePath);
        if (fileInfo.Length > MaxInlineImageBytes)
        {
            return ToolResponse.Error(
                $"Image '{resourceKey}' is {fileInfo.Length} bytes, which exceeds the {MaxInlineImageBytes}-byte inline cap. " +
                $"Resize or recompress the image (or capture a smaller screenshot via webview_screenshot with maxEdge) " +
                $"before calling file_read_image.");
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);

        var metadata = new FileReadImageResult(resourceKey.ToString(), mimeType, bytes.Length);
        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        return ToolResponse.SuccessWithImage(bytes, mimeType, metadataJson);
    }
}
