using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by explorer_extract with the extraction summary.
/// </summary>
public record class ExplorerExtractResult(int Entries, string Destination, int Skipped);

public partial class ExplorerTools
{
    /// <summary>Extract a zip archive into a destination folder in the project tree.</summary>
    [McpServerTool(Name = "explorer_extract")]
    [ToolAlias("explorer.extract")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Extract(
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

        var extractResultWrapper = await ExecuteCommandAsync<IExtractArchiveCommand, ExtractArchiveResult>(command =>
        {
            command.ArchiveResource = archiveKey;
            command.DestinationResource = destinationKey;
            command.Overwrite = overwrite;
        });

        if (extractResultWrapper.IsFailure)
        {
            return ToolResponse.Error(extractResultWrapper);
        }

        var extractResult = extractResultWrapper.Value;
        var result = new ExplorerExtractResult(
            extractResult.Entries,
            extractResult.Destination,
            extractResult.SkippedEntries.Count);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
