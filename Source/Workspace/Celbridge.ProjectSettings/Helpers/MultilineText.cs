namespace Celbridge.ProjectSettings.Helpers;

/// <summary>
/// Reads a multi-line text field as a list of entries, one per line. A text box hands its text back with
/// carriage returns as the line separator, and a block pasted from elsewhere can carry any line ending, so
/// every form is split on rather than one.
/// </summary>
internal static class MultilineText
{
    /// <summary>
    /// The non-blank lines of the text, each trimmed.
    /// </summary>
    public static IReadOnlyList<string> ParseLines(string text)
    {
        return text
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The entries as one block of text, a line each.
    /// </summary>
    public static string FormatLines(IEnumerable<string> values)
    {
        return string.Join("\n", values);
    }
}
