namespace Celbridge.Resources.Commands;

/// <summary>
/// Helper methods for line deletion operations, extracted for testability.
/// </summary>
public static class DeleteLinesHelper
{
    /// <summary>
    /// Creates a TextEdit that deletes the specified line range from a document.
    /// When the range does not include the last line, the edit spans from the start
    /// of the first line to the start of the line after the range, cleanly removing
    /// the lines and their terminators. When the range includes the last line,
    /// the edit spans to the end of the last line instead.
    /// </summary>
    public static TextEdit CreateDeleteEdit(int startLine, int endLine, int totalLineCount)
    {
        if (endLine < totalLineCount)
        {
            return TextEdit.Delete(startLine, 1, endLine + 1, 1);
        }

        return TextEdit.Delete(startLine, 1, endLine, int.MaxValue);
    }

    /// <summary>
    /// Deletes lines from a list of strings using 1-based line numbers (inclusive).
    /// Returns a result indicating success or failure with an error message.
    /// </summary>
    public static Result DeleteLinesFromList(List<string> lines, int startLine, int endLine)
    {
        var startIndex = startLine - 1;
        var endIndex = endLine - 1;

        if (startIndex < 0 || startIndex >= lines.Count)
        {
            return Result.Fail($"Start line {startLine} is out of range (file has {lines.Count} lines)");
        }

        if (endIndex < 0 || endIndex >= lines.Count)
        {
            return Result.Fail($"End line {endLine} is out of range (file has {lines.Count} lines)");
        }

        var deleteCount = endIndex - startIndex + 1;
        lines.RemoveRange(startIndex, deleteCount);

        return Result.Ok();
    }
}
