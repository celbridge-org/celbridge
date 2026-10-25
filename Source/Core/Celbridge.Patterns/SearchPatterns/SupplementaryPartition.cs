namespace Celbridge.Patterns;

/// <summary>
/// Sorts the code points above U+FFFF into groups that no set in a regex tells apart. Before matching, each high
/// surrogate in the text is replaced with the marker of its pair's group. A set then needs only two small classes
/// for its characters above U+FFFF. The replacement keeps every offset in the text.
/// </summary>
internal sealed class SupplementaryPartition
{
    private const char FirstMarker = '\uD800';

    // The first code point of each interval, in order, and the group of each interval.
    private readonly int[] _intervalStarts;
    private readonly int[] _intervalGroups;

    public SupplementaryPartition(int[] intervalStarts, int[] intervalGroups)
    {
        _intervalStarts = intervalStarts;
        _intervalGroups = intervalGroups;
    }

    public static char MarkerOf(int group)
    {
        return (char)(FirstMarker + group);
    }

    /// <summary>
    /// Returns the text with the high surrogate of each pair replaced by the marker of the pair's group.
    /// </summary>
    public string Encode(string text)
    {
        var firstSurrogate = text.AsSpan().IndexOfAnyInRange('\uD800', '\uDBFF');
        if (firstSurrogate < 0)
        {
            return text;
        }

        var characters = text.ToCharArray();
        for (var index = firstSurrogate; index + 1 < characters.Length; index++)
        {
            if (!char.IsHighSurrogate(characters[index])
                || !char.IsLowSurrogate(characters[index + 1]))
            {
                continue;
            }

            var codePoint = char.ConvertToUtf32(characters[index], characters[index + 1]);
            characters[index] = MarkerOf(GroupOf(codePoint));
            index++;
        }

        return new string(characters);
    }

    private int GroupOf(int codePoint)
    {
        var index = Array.BinarySearch(_intervalStarts, codePoint);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return _intervalGroups[index];
    }
}
