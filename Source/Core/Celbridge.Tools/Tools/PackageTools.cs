using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for package operations: archiving and unarchiving files and folders.
/// </summary>
[McpServerToolType]
public partial class PackageTools : AgentToolBase
{
    public PackageTools(IApplicationServiceProvider services) : base(services) { }

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
        var (callToolResult, archiveResult) = await ExecuteCommandAsync<IArchiveResourceCommand, ArchiveResult>(command =>
        {
            command.SourceResource = resource;
            command.ArchiveResource = archive;
            command.Include = include;
            command.Exclude = exclude;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || archiveResult is null)
        {
            return callToolResult;
        }

        return SuccessResult(JsonSerializer.Serialize(new
        {
            entries = archiveResult.Entries,
            size = archiveResult.Size,
            archive = archiveResult.Archive
        }));
    }

    /// <summary>
    /// Extracts a zip archive to a destination folder.
    /// </summary>
    /// <param name="archive">Resource key of the zip file to extract.</param>
    /// <param name="destination">Resource key of the target folder.</param>
    /// <param name="overwrite">Whether to overwrite existing files. Default is false.</param>
    /// <returns>JSON object with fields: entries (int), destination (string).</returns>
    [McpServerTool(Name = "package_unarchive")]
    [ToolAlias("package.unarchive")]
    public async partial Task<CallToolResult> Unarchive(
        string archive,
        string destination,
        bool overwrite = false)
    {
        var (callToolResult, unarchiveResult) = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
        {
            command.ArchiveResource = archive;
            command.DestinationResource = destination;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || unarchiveResult is null)
        {
            return callToolResult;
        }

        return SuccessResult(JsonSerializer.Serialize(new
        {
            entries = unarchiveResult.Entries,
            destination = unarchiveResult.Destination
        }));
    }
}
