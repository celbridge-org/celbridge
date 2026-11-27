namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Contains text split around a marked part. For example, the mark can be the part of a path that matched a search.
/// A view draws the three parts as runs on one line, and styles the mark.
/// </summary>
public record MarkedText(string Before, string Mark, string After)
{
    /// <summary>
    /// Returns text with no mark. The whole text goes in Before.
    /// </summary>
    public static MarkedText Unmarked(string text)
    {
        return new MarkedText(text, string.Empty, string.Empty);
    }

    /// <summary>
    /// Splits text around a mark. The start and length count characters in the text, and the mark must lie inside
    /// the text.
    /// </summary>
    public static MarkedText Create(string text, int markStart, int markLength)
    {
        Guard.IsInRange(markStart, 0, text.Length);
        Guard.IsInRange(markLength, 0, text.Length - markStart);

        var markEnd = markStart + markLength;

        return new MarkedText(
            text[..markStart],
            text[markStart..markEnd],
            text[markEnd..]);
    }
}
