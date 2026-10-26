namespace Celbridge.Search;

/// <summary>
/// A match of a text matcher in a line. Start and Length count UTF-16 characters in the line.
/// </summary>
public readonly record struct TextMatch(int Start, int Length);

/// <summary>
/// Finds the matches of a compiled search pattern in a line of text, the way an IPathMatcher matches paths.
/// </summary>
public interface ITextMatcher
{
    /// <summary>
    /// Returns the pattern's text, as written.
    /// </summary>
    string Pattern { get; }

    /// <summary>
    /// Returns the matches in a line, in order. Matches never overlap.
    /// </summary>
    IReadOnlyList<TextMatch> FindMatches(string line);
}
