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
    /// <summary>Zip a file or folder into a project-tree archive (folder contents at archive root).</summary>
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
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ToolResponse.InvalidResourceKey(archive);
        }

        var archiveResultWrapper = await ExecuteCommandAsync<IArchiveResourceCommand, ArchiveResult>(command =>
        {
            command.SourceResource = resourceKey;
            command.ArchiveResource = archiveKey;
            command.Include = include;
            command.Exclude = exclude;
            command.Overwrite = overwrite;
        });

        if (archiveResultWrapper.IsFailure)
        {
            return ToolResponse.Error(archiveResultWrapper);
        }

        var archiveResult = archiveResultWrapper.Value;
        var result = new PackageArchiveResult(archiveResult.Entries, archiveResult.Size, archiveResult.Archive);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
