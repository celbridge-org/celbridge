using System.Text;

namespace Celbridge.DesignTokens;

/// <summary>
/// Lays prose out as a wrapped comment block. Comments are stored in the token source as unwrapped
/// paragraphs, so the wrapping decision lives here rather than in the source file.
/// </summary>
public static class CommentFormatter
{
    private const int MaxLineLength = 108;

    /// <summary>
    /// Formats paragraphs as one comment block, wrapped to fit the line budget and separated by blank
    /// lines. Returns no lines when there is no prose to emit.
    /// </summary>
    public static IReadOnlyList<string> FormatBlock(
        IReadOnlyList<string> paragraphs,
        string indent,
        string opener,
        string closer)
    {
        if (paragraphs.Count == 0)
        {
            return [];
        }

        var continuationIndent = indent + new string(' ', opener.Length);
        var availableWidth = MaxLineLength - continuationIndent.Length;
        var lines = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            foreach (var wrappedLine in WrapText(paragraph, availableWidth))
            {
                var prefix = lines.Count == 0 ? indent + opener : continuationIndent;
                lines.Add(prefix + wrappedLine);
            }
        }

        lines[^1] += closer;

        return lines;
    }

    /// <summary>
    /// Rewrites text so it is safe inside an XML comment, which cannot contain a double hyphen. Reducing
    /// each run to a single hyphen keeps a CSS custom property name recognisable.
    /// </summary>
    public static string EscapeForXml(string text)
    {
        var escaped = text;
        while (escaped.Contains("--"))
        {
            escaped = escaped.Replace("--", "-");
        }

        return escaped;
    }

    private static IReadOnlyList<string> WrapText(string text, int availableWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in words)
        {
            var lengthWithWord = line.Length == 0 ? word.Length : line.Length + 1 + word.Length;
            if (line.Length > 0 &&
                lengthWithWord > availableWidth)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }
}
