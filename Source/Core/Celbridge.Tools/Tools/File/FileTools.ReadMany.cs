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
    public async partial Task<CallToolResult> ReadMany(string resources, int offset = 0, int limit = 0)
    {
        List<string>? resourceKeys;
        try
        {
            resourceKeys = JsonSerializer.Deserialize<List<string>>(resources);
        }
        catch (JsonException ex)
        {
            return ToolError($"Invalid JSON array: {ex.Message}");
        }

        if (resourceKeys is null || resourceKeys.Count == 0)
        {
            return ToolError("No resource keys provided.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var entries = new List<ReadManyFileEntry>();
        foreach (var resourceString in resourceKeys)
        {
            if (!ResourceKey.TryCreate(resourceString, out var resourceKey))
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"Invalid resource key: '{resourceString}'"));
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
            if (resolveResult.IsFailure)
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"Failed to resolve path for resource: '{resourceString}'"));
                continue;
            }
            var resourcePath = resolveResult.Value;

            if (!File.Exists(resourcePath))
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"File not found: '{resourceString}'"));
                continue;
            }

            var fileText = await File.ReadAllTextAsync(resourcePath);
            var totalLineCount = LineEndingHelper.CountLines(fileText);

            if (offset == 0 && limit == 0)
            {
                // Preserve raw line endings as they exist on disk.
                entries.Add(new ReadManyFileEntry(resourceString, Content: fileText, TotalLineCount: totalLineCount));
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
                    entries.Add(new ReadManyFileEntry(resourceString, Content: string.Empty, TotalLineCount: totalLineCount));
                }
                else
                {
                    var selectedLines = allLines.Skip(startIndex).Take(count);
                    var content = string.Join(fileSeparator, selectedLines);
                    entries.Add(new ReadManyFileEntry(resourceString, Content: content, TotalLineCount: totalLineCount));
                }
            }
        }

        var result = new ReadManyResult(entries);
        return ToolSuccess(SerializeJson(result));
    }
}
