using Celbridge.Utilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_read when offset or limit are specified.
/// </summary>
public record class FileReadResult(string Content, int TotalLineCount);

public partial class FileTools
{
    /// <summary>
    /// Reads the text content of a file. Always returns JSON with content and totalLineCount.
    /// Supports optional line range via offset and limit.
    /// For large files, use file_get_info first to check line count and size before reading.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <param name="offset">Starting line number (1-based). Use 0 to read from the beginning.</param>
    /// <param name="limit">Maximum number of lines to return. Use 0 to read to the end.</param>
    /// <param name="lineNumbers">When true, prefix each line in content with its 1-based line number (e.g. "1: first line"). Line numbers reflect actual positions in the file, even when using offset.</param>
    /// <returns>JSON with fields: content (string), totalLineCount (int).</returns>
    [McpServerTool(Name = "file_read", ReadOnly = true)]
    [ToolAlias("file.read")]
    public async partial Task<CallToolResult> Read(string resource, int offset = 0, int limit = 0, bool lineNumbers = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        var fileText = await File.ReadAllTextAsync(resourcePath);
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
            return SuccessResult(SerializeJson(wholeFileResult));
        }

        var allLines = LineEndingHelper.SplitToContentLines(fileText);
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : allLines.Count - startIndex;
        count = Math.Min(count, allLines.Count - startIndex);

        if (startIndex >= allLines.Count)
        {
            var emptyResult = new FileReadResult(string.Empty, totalLineCount);
            return SuccessResult(SerializeJson(emptyResult));
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

        var readResult = new FileReadResult(rangeContent, totalLineCount);
        return SuccessResult(SerializeJson(readResult));
    }
}
