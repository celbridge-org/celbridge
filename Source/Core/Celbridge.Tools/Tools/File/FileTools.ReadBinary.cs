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
    /// <summary>
    /// Reads a binary file and returns its content as base64 with MIME type.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <returns>JSON object with fields: base64 (string), mimeType (string), size (int).</returns>
    [McpServerTool(Name = "file_read_binary", ReadOnly = true)]
    [ToolAlias("file.read_binary")]
    public async partial Task<CallToolResult> ReadBinary(string resource)
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

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        var result = new FileReadBinaryResult(base64, mimeType, bytes.Length);
        return SuccessResult(SerializeJson(result));
    }
}
