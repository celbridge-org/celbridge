namespace Celbridge.Resources.Helpers;

/// <summary>
/// A point in a text file, in one-based line and column numbers.
/// </summary>
public record TextPosition(int Line, int Column);

/// <summary>
/// Converts character offsets in a body of text to line and column numbers. Built once per file and
/// reused for every offset in it, so a file with many offsets is still scanned once.
/// </summary>
public sealed class TextLineIndex
{
    // The offset each line starts at, in ascending order. Line 1 always starts at offset 0.
    private readonly List<int> _lineStartOffsets;

    public TextLineIndex(string text)
    {
        _lineStartOffsets = new List<int>
        {
            0
        };

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            _lineStartOffsets.Add(index + 1);
        }
    }

    /// <summary>
    /// The line and column the given character offset falls on. An offset past the end of the text
    /// resolves to the last line.
    /// </summary>
    public TextPosition Resolve(int offset)
    {
        if (offset <= 0)
        {
            return new TextPosition(1, 1);
        }

        // BinarySearch returns the index of an exact match, or the bitwise complement of the index of
        // the next larger element, which is one past the line the offset falls on.
        int searchResult = _lineStartOffsets.BinarySearch(offset);
        int lineIndex = searchResult >= 0
            ? searchResult
            : ~searchResult - 1;

        int column = offset - _lineStartOffsets[lineIndex] + 1;

        return new TextPosition(lineIndex + 1, column);
    }
}
