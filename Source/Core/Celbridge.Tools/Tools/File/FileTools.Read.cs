using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_read when offset or limit are specified.
/// </summary>
public record class FileReadResult(string Content, int TotalLineCount);

public partial class FileTools
{
    /// <summary>Read the text content of one file, with optional line offset, limit, and line-number prefixes.</summary>
    [McpServerTool(Name = "file_read", ReadOnly = true)]
    [ToolAlias("file.read")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Read(string resource, int offset = 0, int limit = 0, bool lineNumbers = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceFileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(resourceKey);
        if (infoResult.IsFailure)
        {
            // Surface the gateway's failure verbatim so case-mismatch
            // errors (which carry the canonical key) reach the caller. The
            // generic "resource not found" message only fires when the
            // resolve succeeded but the resource genuinely is not a file.
            return ToolResponse.Error(infoResult);
        }
        if (infoResult.Value.Kind != StorageItemKind.File)
        {
            return ToolResponse.Error($"Resource not found: '{resourceKey}'. file_read addresses resources by resource key, not arbitrary disk paths — only files under a registered root (e.g. 'project:', 'temp:', 'logs:') can be read.");
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(resourceKey);
        if (readResult.IsFailure)
        {
            return ToolResponse.Error(readResult.FirstErrorMessage);
        }
        var fileText = readResult.Value;
        var totalLineCount = LineEndingHelper.CountLines(fileText);
        var fileSeparator = LineEndingHelper.DetectSeparatorOrDefault(fileText);

        if (offset == 0 && limit == 0)
        {
            string content;
            if (lineNumbers)
            {
                var contentLines = LineEndingHelper.SplitToContentLines(fileText);
                content = FileReadHelper.AddLineNumbers(contentLines, 1, fileSeparator);
            }
            else
            {
                // Preserve raw line endings as they exist on disk.
                content = fileText;
            }

            var wholeFileResult = new FileReadResult(content, totalLineCount);
            return ToolResponse.Success(SerializeJson(wholeFileResult));
        }

        var allLines = LineEndingHelper.SplitToContentLines(fileText);
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : allLines.Count - startIndex;
        count = Math.Min(count, allLines.Count - startIndex);

        if (startIndex >= allLines.Count)
        {
            var emptyResult = new FileReadResult(string.Empty, totalLineCount);
            return ToolResponse.Success(SerializeJson(emptyResult));
        }

        var selectedLines = allLines.Skip(startIndex).Take(count).ToList();
        string rangeContent;

        if (lineNumbers)
        {
            var firstLineNumber = startIndex + 1;
            rangeContent = FileReadHelper.AddLineNumbers(selectedLines, firstLineNumber, fileSeparator);
        }
        else
        {
            rangeContent = string.Join(fileSeparator, selectedLines);
        }

        var rangeReadResult = new FileReadResult(rangeContent, totalLineCount);
        return ToolResponse.Success(SerializeJson(rangeReadResult));
    }
}
