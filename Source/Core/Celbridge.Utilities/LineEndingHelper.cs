namespace Celbridge.Utilities;

/// <summary>
/// Helpers for detecting line-ending conventions and counting logical lines.
/// The strategy is to preserve the existing file's line endings on edit and
/// use the platform default when creating new files. Line counting follows
/// File.ReadAllLines semantics: a file is N lines if it has N content rows,
/// regardless of whether it ends with a terminating newline.
/// </summary>
public static class LineEndingHelper
{
    /// <summary>
    /// The platform line separator: CRLF on Windows, LF on Unix-like systems.
    /// Used as the default for new files.
    /// </summary>
    public static string PlatformDefault => Environment.NewLine;

    /// <summary>
    /// Returns the line separator that best represents the given content.
    /// For empty or single-line content, returns the platform default. For
    /// mixed content, returns whichever style appears more often, falling
    /// back to the platform default on a tie.
    /// </summary>
    public static string DetectSeparatorOrDefault(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return PlatformDefault;
        }

        var crLfCount = CountOccurrences(content, "\r\n");
        var loneLfCount = CountLoneLineFeeds(content);

        if (crLfCount == 0 && loneLfCount == 0)
        {
            return PlatformDefault;
        }

        if (crLfCount > loneLfCount)
        {
            return "\r\n";
        }

        if (loneLfCount > crLfCount)
        {
            return "\n";
        }

        return PlatformDefault;
    }

    /// <summary>
    /// Returns true if the content's last character is a line terminator,
    /// indicating the file ends with a newline. Used to preserve trailing-
    /// newline state across edits.
    /// </summary>
    public static bool EndsWithNewline(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        return content[^1] == '\n';
    }

    /// <summary>
    /// Splits content into logical lines without line terminators. Handles
    /// both \n and \r\n inputs. A single trailing empty element introduced by
    /// a terminating newline is dropped, so the returned list models content
    /// only — callers re-add a terminating newline at write time based on
    /// EndsWithNewline.
    /// </summary>
    public static List<string> SplitToContentLines(string content)
    {
        if (content.Length == 0)
        {
            return new List<string>();
        }

        var split = content.Split('\n');

        for (var i = 0; i < split.Length; i++)
        {
            if (split[i].Length > 0 && split[i][^1] == '\r')
            {
                split[i] = split[i][..^1];
            }
        }

        if (split.Length > 0 && split[^1].Length == 0)
        {
            return new List<string>(split.Take(split.Length - 1));
        }

        return new List<string>(split);
    }

    /// <summary>
    /// Rewrites the input so every line break is exactly the target separator.
    /// Accepts mixed input (\n, \r\n, or lone \r) and collapses to the target
    /// without the double-replacement trap of `\n -> targetWithLf -> next \n`
    /// when the target is \r\n. Empty or null input is returned unchanged.
    /// </summary>
    public static string ConvertLineEndings(string text, string targetSeparator)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Collapse every variant to a lone \n, then expand to the target. The
        // \r\n pass must run before the lone-\r pass to avoid splitting a CRLF
        // pair into two separate line breaks.
        var normalized = text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        if (targetSeparator == "\n")
        {
            return normalized;
        }

        return normalized.Replace("\n", targetSeparator);
    }

    /// <summary>
    /// Returns the canonical line count for content, matching File.ReadAllLines
    /// semantics. Empty content is 0 lines. A trailing terminating newline
    /// does not add a phantom empty line.
    /// </summary>
    public static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var newlineCount = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                newlineCount++;
            }
        }

        if (EndsWithNewline(content))
        {
            return newlineCount;
        }

        return newlineCount + 1;
    }

    private static int CountOccurrences(string content, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    private static int CountLoneLineFeeds(string content)
    {
        var count = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n')
            {
                continue;
            }

            if (i == 0 || content[i - 1] != '\r')
            {
                count++;
            }
        }
        return count;
    }
}
