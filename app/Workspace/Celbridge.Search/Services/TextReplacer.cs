namespace Celbridge.Search.Services;

/// <summary>
/// Handles text replacement operations using TextMatcher for finding matches.
/// </summary>
public class TextReplacer
{
    private readonly TextMatcher _textMatcher;

    public TextReplacer()
    {
        _textMatcher = new TextMatcher();
    }

    /// <summary>
    /// Replaces all occurrences of searchText with replaceText in the given content.
    /// Returns the modified content and the number of replacements made.
    /// </summary>
    public (string Content, int ReplacementCount) ReplaceAll(
        string content,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchText))
        {
            return (content, 0);
        }

        var lines = content.Split('\n');
        var replacedLines = new List<string>();
        int totalReplacements = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var carriageReturn = rawLine.EndsWith('\r') ? "\r" : string.Empty;

            var matches = _textMatcher.FindMatches(line, searchText, matchCase, wholeWord);

            if (matches.Count == 0)
            {
                replacedLines.Add(rawLine);
                continue;
            }

            // Replace matches from end to start to preserve indices
            var modifiedLine = line;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                modifiedLine = modifiedLine.Substring(0, match.Start) +
                              replaceText +
                              modifiedLine.Substring(match.Start + match.Length);
                totalReplacements++;
            }

            replacedLines.Add(modifiedLine + carriageReturn);
        }

        var newContent = string.Join("\n", replacedLines);
        return (newContent, totalReplacements);
    }

    /// <summary>
    /// Replaces a single match at a specific line and position.
    /// Returns the modified content and whether the replacement succeeded.
    /// </summary>
    public (string Content, bool Success) ReplaceMatch(
        string content,
        string searchText,
        string replaceText,
        int lineNumber,
        int originalMatchStart,
        bool matchCase,
        bool wholeWord)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchText))
        {
            return (content, false);
        }

        var lines = content.Split('\n');
        var lineIndex = lineNumber - 1;

        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return (content, false);
        }

        var rawLine = lines[lineIndex];
        var line = rawLine.TrimEnd('\r');
        var carriageReturn = rawLine.EndsWith('\r') ? "\r" : string.Empty;

        // Find the specific match at the expected position
        var matches = _textMatcher.FindMatches(line, searchText, matchCase, wholeWord);
        var targetMatch = matches.FirstOrDefault(m => m.Start == originalMatchStart);

        if (targetMatch == default)
        {
            return (content, false);
        }

        var modifiedLine = line.Substring(0, targetMatch.Start) +
                           replaceText +
                           line.Substring(targetMatch.Start + targetMatch.Length);
        lines[lineIndex] = modifiedLine + carriageReturn;

        var newContent = string.Join("\n", lines);
        return (newContent, true);
    }
}
