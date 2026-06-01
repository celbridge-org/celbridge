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
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> ReadBinary(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceFileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(resourceKey);
        if (infoResult.IsFailure)
        {
            return ToolResponse.Error(infoResult);
        }
        if (infoResult.Value.Kind != StorageItemKind.File)
        {
            return ToolResponse.Error($"File not found: '{resourceKey}'");
        }

        var bytesResult = await resourceFileSystem.ReadAllBytesAsync(resourceKey);
        if (bytesResult.IsFailure)
        {
            return ToolResponse.Error(bytesResult.FirstErrorMessage);
        }
        var bytes = bytesResult.Value;
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourceKey.Path).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        var result = new FileReadBinaryResult(base64, mimeType, bytes.Length);
        return ToolResponse.Success(SerializeJson(result));
    }
}
