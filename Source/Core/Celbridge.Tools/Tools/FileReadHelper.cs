namespace Celbridge.Tools;

/// <summary>
/// Helper methods for file read operations, extracted for testability.
/// </summary>
public static class FileReadHelper
{
    /// <summary>
    /// Prefixes each line with its 1-based line number and a separator.
    /// </summary>
    public static string AddLineNumbers(string[] lines, int startLineNumber)
    {
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var numberedLines = new string[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            var lineNumber = startLineNumber + index;
            numberedLines[index] = $"{lineNumber}: {lines[index]}";
        }

        return string.Join(Environment.NewLine, numberedLines);
    }

    /// <summary>
    /// Counts the number of lines in a text string.
    /// Uses newline splitting consistent with File.ReadAllLines behavior.
    /// </summary>
    public static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        return text.Split('\n').Length;
    }
}
