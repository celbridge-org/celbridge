using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_archive with the archive summary.
/// </summary>
public record class PackageArchiveResult(int Entries, long Size, string Archive);

public partial class PackageTools
{
    /// <summary>
    /// Creates a zip archive from a file or folder. When archiving a folder, the archive
    /// contains the folder's contents at the root, not the folder itself.
    /// </summary>
    /// <param name="resource">Resource key of the file or folder to archive.</param>
    /// <param name="archive">Resource key for the output zip file.</param>
    /// <param name="include">Optional semicolon-separated glob patterns to include (e.g. "*.py;*.md"). When empty, all files are included.</param>
    /// <param name="exclude">Optional semicolon-separated glob patterns to exclude (e.g. "__pycache__;.git").</param>
    /// <param name="overwrite">Whether to overwrite an existing archive. Default is false.</param>
    /// <returns>JSON object with fields: entries (int), size (long), archive (string).</returns>
    [McpServerTool(Name = "package_archive")]
    [ToolAlias("package.archive")]
    public async partial Task<CallToolResult> Archive(
        string resource,
        string archive,
        string include = "",
        string exclude = "",
        bool overwrite = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ErrorResult($"Invalid resource key: '{archive}'");
        }

        var (callToolResult, archiveResult) = await ExecuteCommandAsync<IArchiveResourceCommand, ArchiveResult>(command =>
        {
            command.SourceResource = resourceKey;
            command.ArchiveResource = archiveKey;
            command.Include = include;
            command.Exclude = exclude;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || archiveResult is null)
        {
            return callToolResult;
        }

        var result = new PackageArchiveResult(archiveResult.Entries, archiveResult.Size, archiveResult.Archive);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
