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

        if (offset == 0 && limit == 0)
        {
            var text = await File.ReadAllTextAsync(resourcePath);
            var lineCount = FileReadHelper.CountLines(text);

            if (lineNumbers)
            {
                var splitLines = text.Split('\n');
                text = FileReadHelper.AddLineNumbers(splitLines, 1);
            }

            var wholeFileResult = new FileReadResult(text, lineCount);
            return SuccessResult(SerializeJson(wholeFileResult));
        }

        var lines = await File.ReadAllLinesAsync(resourcePath);
        var totalLineCount = lines.Length;
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : lines.Length - startIndex;
        count = Math.Min(count, lines.Length - startIndex);

        if (startIndex >= lines.Length)
        {
            var emptyResult = new FileReadResult(string.Empty, totalLineCount);
            return SuccessResult(SerializeJson(emptyResult));
        }

        var selectedLines = lines.Skip(startIndex).Take(count).ToArray();
        string content;

        if (lineNumbers)
        {
            var firstLineNumber = startIndex + 1;
            content = FileReadHelper.AddLineNumbers(selectedLines, firstLineNumber);
        }
        else
        {
            content = string.Join(Environment.NewLine, selectedLines);
        }

        var readResult = new FileReadResult(content, totalLineCount);
        return SuccessResult(SerializeJson(readResult));
    }
}
