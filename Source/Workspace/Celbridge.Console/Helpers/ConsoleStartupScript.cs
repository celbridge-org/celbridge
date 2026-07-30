namespace Celbridge.Console.Helpers;

/// <summary>
/// Splits a console's startup script into the lines injected into its pty.
/// </summary>
public static class ConsoleStartupScript
{
    public static IReadOnlyList<string> SplitLines(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Array.Empty<string>();
        }

        var normalized = script.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        // A trailing blank line is file formatting rather than a line to submit. Interior blanks are kept:
        // at a REPL prompt a blank line is what closes an indented block.
        while (lines.Count > 0
            && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
