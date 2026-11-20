using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Applies range edits to a file's text. The text keeps the file's line endings and terminating newline, and each
/// edit's new text takes the file's line separator.
/// </summary>
public static class RangeEditHelper
{
    /// <summary>
    /// Returns the text with the edits applied. Each edit's range refers to the original text. Fails when a range
    /// falls outside the text's lines.
    /// </summary>
    public static Result<string> ApplyEdits(string content, IEnumerable<RangeEdit> edits)
    {
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var endsWithNewline = LineEndingHelper.EndsWithNewline(content);

        // The line list models content only. A terminating newline is added again at the end.
        var lines = LineEndingHelper.SplitToContentLines(content);

        // Apply the edits from the bottom of the text to the top, so an edit never moves the range of an edit
        // still to come.
        var sortedEdits = edits
            .OrderByDescending(edit => edit.Line)
            .ThenByDescending(edit => edit.Column)
            .ToList();

        foreach (var edit in sortedEdits)
        {
            var startLine = edit.Line - 1;
            var startColumn = edit.Column - 1;
            var endLine = edit.EndLine - 1;

            if (startLine < 0 || startLine >= lines.Count)
            {
                return Result.Fail($"Edit start line {edit.Line} is out of range (file has {lines.Count} lines)");
            }

            if (endLine < 0 || endLine >= lines.Count)
            {
                return Result.Fail($"Edit end line {edit.EndLine} is out of range (file has {lines.Count} lines)");
            }

            // An end column of -1 means the end of the line.
            var endColumn = edit.EndColumn == -1
                ? lines[endLine].Length
                : edit.EndColumn - 1;

            var beforeEdit = lines[startLine].Substring(0, Math.Min(startColumn, lines[startLine].Length));

            var afterEdit = endColumn <= lines[endLine].Length
                ? lines[endLine].Substring(endColumn)
                : string.Empty;

            // The new text is split on lone line feeds, and the lines are joined with the text's separator. So
            // a carriage return never doubles.
            var normalizedNewText = LineEndingHelper.ConvertLineEndings(edit.NewText, "\n");
            var newContent = beforeEdit + normalizedNewText + afterEdit;
            var newLines = newContent.Split('\n');

            var lineCount = endLine - startLine + 1;
            lines.RemoveRange(startLine, lineCount);
            lines.InsertRange(startLine, newLines);
        }

        var output = string.Join(separator, lines);
        if (endsWithNewline && output.Length > 0)
        {
            output += separator;
        }

        return output;
    }
}
