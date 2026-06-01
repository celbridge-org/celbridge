using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Top-level result returned by file_read_many.
/// </summary>
public record class ReadManyResult(List<ReadManyFileEntry> Files);

/// <summary>
/// A per-file entry in the file_read_many response. Contains either content on success or an error message on failure.
/// </summary>
public record class ReadManyFileEntry(string Resource, string? Content = null, int? TotalLineCount = null, string? Error = null);

public partial class FileTools
{
    /// <summary>Batch-read several text files in one call; per-file errors are reported per entry, not as a global failure.</summary>
    [McpServerTool(Name = "file_read_many", ReadOnly = true)]
    [ToolAlias("file.read_many")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> ReadMany(string resources, int offset = 0, int limit = 0)
    {
        List<string>? resourceKeys;
        try
        {
            resourceKeys = JsonSerializer.Deserialize<List<string>>(resources);
        }
        catch (JsonException)
        {
            return ToolResponse.Error(
                "resources must be a JSON array of resource keys, e.g. [\"project:notes/a.md\", \"project:notes/b.md\"].");
        }

        if (resourceKeys is null || resourceKeys.Count == 0)
        {
            return ToolResponse.Error("No resource keys provided.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceFileSystem;

        var entries = new List<ReadManyFileEntry>();
        foreach (var resourceString in resourceKeys)
        {
            if (!ResourceKey.TryCreate(resourceString, out var resourceKey))
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"Invalid resource key: '{resourceString}'"));
                continue;
            }

            // Echo the canonical form of the resource key in per-entry output so that
            // entries for different roots are unambiguous regardless of how the agent typed them.
            var canonicalResource = resourceKey.ToString();

            var infoResult = await resourceFileSystem.GetInfoAsync(resourceKey);
            if (infoResult.IsFailure)
            {
                entries.Add(new ReadManyFileEntry(canonicalResource, Error: infoResult.FirstErrorMessage));
                continue;
            }
            if (infoResult.Value.Kind != StorageItemKind.File)
            {
                entries.Add(new ReadManyFileEntry(canonicalResource, Error: $"File not found: '{canonicalResource}'"));
                continue;
            }

            var readResult = await resourceFileSystem.ReadAllTextAsync(resourceKey);
            if (readResult.IsFailure)
            {
                entries.Add(new ReadManyFileEntry(canonicalResource, Error: readResult.FirstErrorMessage));
                continue;
            }
            var fileText = readResult.Value;
            var totalLineCount = LineEndingHelper.CountLines(fileText);

            if (offset == 0 && limit == 0)
            {
                // Preserve raw line endings as they exist on disk.
                entries.Add(new ReadManyFileEntry(canonicalResource, Content: fileText, TotalLineCount: totalLineCount));
            }
            else
            {
                var allLines = LineEndingHelper.SplitToContentLines(fileText);
                var fileSeparator = LineEndingHelper.DetectSeparatorOrDefault(fileText);
                var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
                var count = limit > 0 ? limit : allLines.Count - startIndex;
                count = Math.Min(count, allLines.Count - startIndex);

                if (startIndex >= allLines.Count)
                {
                    entries.Add(new ReadManyFileEntry(canonicalResource, Content: string.Empty, TotalLineCount: totalLineCount));
                }
                else
                {
                    var selectedLines = allLines.Skip(startIndex).Take(count);
                    var content = string.Join(fileSeparator, selectedLines);
                    entries.Add(new ReadManyFileEntry(canonicalResource, Content: content, TotalLineCount: totalLineCount));
                }
            }
        }

        var result = new ReadManyResult(entries);
        return ToolResponse.Success(SerializeJson(result));
    }
}
