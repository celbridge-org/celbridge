namespace Celbridge.Explorer.Services.Search;

/// <summary>
/// Finds text matches within lines, supporting case-sensitive and whole-word matching.
/// </summary>
public class TextMatcher
{
    /// <summary>
    /// Finds all matches of a search term within a line of text.
    /// </summary>
    public List<(int Start, int Length)> FindMatches(
        string line,
        string searchTerm,
        bool matchCase,
        bool wholeWord)
    {
        var matches = new List<(int Start, int Length)>();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int index = 0;

        while (index < line.Length)
        {
            int foundIndex = line.IndexOf(searchTerm, index, comparison);
            if (foundIndex < 0)
            {
                break;
            }

            bool isMatch = true;

            if (wholeWord)
            {
                isMatch = IsWholeWordMatch(line, foundIndex, searchTerm.Length);
            }

            if (isMatch)
            {
                matches.Add((foundIndex, searchTerm.Length));
            }

            index = foundIndex + 1;
        }

        return matches;
    }

    /// <summary>
    /// Checks if a match at the given position represents a whole word.
    /// </summary>
    private bool IsWholeWordMatch(string line, int start, int length)
    {
        bool startOk = start == 0 || !char.IsLetterOrDigit(line[start - 1]);
        bool endOk = start + length >= line.Length || !char.IsLetterOrDigit(line[start + length]);
        return startOk && endOk;
    }
}
