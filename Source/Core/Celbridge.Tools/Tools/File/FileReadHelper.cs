namespace Celbridge.Tools;

/// <summary>
/// Helper methods for file read operations, extracted for testability.
/// </summary>
public static class FileReadHelper
{
    /// <summary>
    /// Prefixes each line with its 1-based line number and a separator. Lines
    /// are joined using the supplied lineSeparator so the output preserves the
    /// source file's line-ending style. Callers should pass content lines that
    /// have already been stripped of trailing CR.
    /// </summary>
    public static string AddLineNumbers(IReadOnlyList<string> lines, int startLineNumber, string lineSeparator)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var numberedLines = new string[lines.Count];
        for (int index = 0; index < lines.Count; index++)
        {
            var lineNumber = startLineNumber + index;
            numberedLines[index] = $"{lineNumber}: {lines[index]}";
        }

        return string.Join(lineSeparator, numberedLines);
    }

    /// <summary>
    /// Returns the canonical line count for content. Empty content is 0 lines.
    /// A trailing terminating newline does not add a phantom empty line. Matches
    /// File.ReadAllLines and LineEndingHelper.CountLines semantics so all line-
    /// aware tools agree on what "N lines" means.
    /// </summary>
    public static int CountLines(string text)
    {
        return LineEndingHelper.CountLines(text);
    }
}
