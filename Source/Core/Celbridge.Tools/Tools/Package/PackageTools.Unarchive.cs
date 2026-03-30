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
        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ErrorResult($"Invalid resource key: '{archive}'");
        }

        if (!ResourceKey.TryCreate(destination, out var destinationKey))
        {
            return ErrorResult($"Invalid resource key: '{destination}'");
        }

        var (callToolResult, unarchiveResult) = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
        {
            command.ArchiveResource = archiveKey;
            command.DestinationResource = destinationKey;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || unarchiveResult is null)
        {
            return callToolResult;
        }

        var result = new PackageUnarchiveResult(unarchiveResult.Entries, unarchiveResult.Destination);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
