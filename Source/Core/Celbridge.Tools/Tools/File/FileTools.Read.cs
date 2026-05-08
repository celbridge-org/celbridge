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
    public async partial Task<CallToolResult> Read(string resource, int offset = 0, int limit = 0, bool lineNumbers = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ToolError($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ToolError($"Resource not found in project: '{resource}'. Note that file_read addresses project resources, not arbitrary disk paths — files outside the project content root cannot be read.");
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
            return ToolSuccess(SerializeJson(wholeFileResult));
        }

        var allLines = LineEndingHelper.SplitToContentLines(fileText);
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : allLines.Count - startIndex;
        count = Math.Min(count, allLines.Count - startIndex);

        if (startIndex >= allLines.Count)
        {
            var emptyResult = new FileReadResult(string.Empty, totalLineCount);
            return ToolSuccess(SerializeJson(emptyResult));
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
        return ToolSuccess(SerializeJson(readResult));
    }
}
