using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_unarchive with the extraction summary.
/// </summary>
public record class PackageUnarchiveResult(int Entries, string Destination);

public partial class PackageTools
{
    /// <summary>Extract a zip archive into a destination folder in the project tree.</summary>
    [McpServerTool(Name = "package_unarchive")]
    [ToolAlias("package.unarchive")]
    [RelatedGuides("resource_keys", "packages_overview")]
    public async partial Task<CallToolResult> Unarchive(
        string archive,
        string destination,
        bool overwrite = false)
    {
        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ToolResponse.InvalidResourceKey(archive);
        }

        if (!ResourceKey.TryCreate(destination, out var destinationKey))
        {
            return ToolResponse.InvalidResourceKey(destination);
        }

        var unarchiveResultWrapper = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
        {
            command.ArchiveResource = archiveKey;
            command.DestinationResource = destinationKey;
            command.Overwrite = overwrite;
        });

        if (unarchiveResultWrapper.IsFailure)
        {
            return ToolResponse.Error(unarchiveResultWrapper);
        }

        var unarchiveResult = unarchiveResultWrapper.Value;
        var result = new PackageUnarchiveResult(unarchiveResult.Entries, unarchiveResult.Destination);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
