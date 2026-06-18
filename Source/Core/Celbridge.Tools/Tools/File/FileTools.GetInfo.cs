using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_get_info for file resources. Carries size, modified
/// time, extension, text/line metadata, read-only state, paired sidecar status,
/// and an optional content hash. Per-field semantics are documented in the
/// file_get_info tool guide.
/// </summary>
public record class FileInfoResult(
    string Type,
    long Size,
    string Modified,
    string Extension,
    bool IsText,
    int? LineCount,
    bool IsReadOnly,
    string? Sidecar,
    string SidecarStatus,
    string? Hash = null);

/// <summary>
/// Result returned by file_get_info for folder resources.
/// </summary>
public record class FolderInfoResult(string Type, string Modified, bool IsReadOnly);

public partial class FileTools
{
    /// <summary>Get metadata for a single file or folder resource.</summary>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> GetInfo(string resource, bool computeHash = false)
    {
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
            return ToolResponse.Error(getInfoResult);
        }
        var snapshot = getInfoResult.Value;

        if (!snapshot.Exists)
        {
            return ToolResponse.ResourceNotFound(resource);
        }

        if (snapshot.IsFile)
        {
            var sidecarStatusText = snapshot.SidecarStatus switch
            {
                CelParseStatus.Healthy => "healthy",
                CelParseStatus.Broken => "broken",
                _ => "none",
            };

            string? hash = null;
            if (computeHash)
            {
                var hashResult = await ComputeFileHashAsync(resourceKey);
                if (hashResult.IsFailure)
                {
                    return ToolResponse.Error(hashResult);
                }
                hash = hashResult.Value;
            }

            var fileResult = new FileInfoResult(
                "file",
                snapshot.Size,
                snapshot.ModifiedUtc.ToString("o"),
                snapshot.Extension,
                snapshot.IsText,
                snapshot.LineCount,
                snapshot.IsReadOnly,
                snapshot.SidecarKey?.ToString(),
                sidecarStatusText,
                hash);
            return ToolResponse.Success(SerializeJson(fileResult));
        }

        var folderResult = new FolderInfoResult(
            "folder",
            snapshot.ModifiedUtc.ToString("o"),
            snapshot.IsReadOnly);
        return ToolResponse.Success(SerializeJson(folderResult));
    }

    // The hash is read after the snapshot rather than inside the command, so a
    // file changing in the microsecond gap could in principle disagree with the
    // reported Size. For session-mid agent usage that is acceptable; callers
    // needing strict consistency would have to add a hash to the snapshot itself.
    private async Task<Result<string>> ComputeFileHashAsync(ResourceKey resourceKey)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("No project is loaded.");
        }

        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var hashResult = await resourceFileSystem.ComputeHashAsync(resourceKey);
        if (hashResult.IsFailure)
        {
            return hashResult;
        }
        var hash = hashResult.Value;

        // Convert.ToHexString (the gateway's hash) is uppercase; lower it so the
        // value matches the git / sha256sum / workshop content_hash convention an
        // agent compares against.
        return hash.ToLowerInvariant();
    }
}
