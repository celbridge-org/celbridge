namespace Celbridge.Patterns;

/// <summary>
/// An immutable set of Unicode scalar values, stored as sorted ranges. A surrogate code point is never a member.
/// </summary>
internal sealed class CodePointSet
{
    public const int MaxCodePoint = 0x10FFFF;
    public const int FirstSupplementary = 0x10000;

    private const int FirstSurrogate = 0xD800;
    private const int LastSurrogate = 0xDFFF;

    // Each range's first and last code point, in order. Neighboring ranges have a gap between them.
    private readonly int[] _bounds;

    public static CodePointSet Empty { get; } = new(Array.Empty<int>());

    public static CodePointSet All { get; } = FromRange(0, MaxCodePoint);

    private CodePointSet(int[] bounds)
    {
        _bounds = bounds;
    }

    public int RangeCount => _bounds.Length / 2;

    public bool IsEmpty => _bounds.Length == 0;

    public int RangeFirst(int rangeIndex) => _bounds[rangeIndex * 2];

    public int RangeLast(int rangeIndex) => _bounds[rangeIndex * 2 + 1];

    public static CodePointSet FromCodePoint(int codePoint)
    {
        return FromRange(codePoint, codePoint);
    }

    public static CodePointSet FromRange(int first, int last)
    {
        var builder = new Builder();
        builder.Add(first, last);

        return builder.ToSet();
    }

    /// <summary>
    /// Builds a set from pairs of a range's first and last code point.
    /// </summary>
    public static CodePointSet FromRangePairs(ReadOnlySpan<int> bounds)
    {
        var builder = new Builder();
        for (var index = 0; index + 1 < bounds.Length; index += 2)
        {
            builder.Add(bounds[index], bounds[index + 1]);
        }

        return builder.ToSet();
    }

    public bool Contains(int codePoint)
    {
        var low = 0;
        var high = RangeCount - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < RangeFirst(middle))
            {
                high = middle - 1;
            }
            else if (codePoint > RangeLast(middle))
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true and the code point when the set has exactly one member.
    /// </summary>
    public bool TryGetSingle(out int codePoint)
    {
        if (RangeCount == 1
            && RangeFirst(0) == RangeLast(0))
        {
            codePoint = RangeFirst(0);
            return true;
        }

        codePoint = 0;
        return false;
    }

    public CodePointSet Union(CodePointSet other)
    {
        var builder = new Builder();
        builder.Add(this);
        builder.Add(other);

        return builder.ToSet();
    }

    public CodePointSet Intersect(CodePointSet other)
    {
        var builder = new Builder();
        var index = 0;
        var otherIndex = 0;
        while (index < RangeCount
            && otherIndex < other.RangeCount)
        {
            var first = Math.Max(RangeFirst(index), other.RangeFirst(otherIndex));
            var last = Math.Min(RangeLast(index), other.RangeLast(otherIndex));
            if (first <= last)
            {
                builder.Add(first, last);
            }

            if (RangeLast(index) < other.RangeLast(otherIndex))
            {
                index++;
            }
            else
            {
                otherIndex++;
            }
        }

        return builder.ToSet();
    }

    public CodePointSet Except(CodePointSet other)
    {
        return Intersect(other.Complement());
    }

    public CodePointSet SymmetricExcept(CodePointSet other)
    {
        var union = Union(other);
        var intersection = Intersect(other);

        return union.Except(intersection);
    }

    public CodePointSet Complement()
    {
        var builder = new Builder();
        var next = 0;
        for (var index = 0; index < RangeCount; index++)
        {
            if (RangeFirst(index) > next)
            {
                builder.Add(next, RangeFirst(index) - 1);
            }
            next = RangeLast(index) + 1;
        }

        if (next <= MaxCodePoint)
        {
            builder.Add(next, MaxCodePoint);
        }

        return builder.ToSet();
    }

    public bool IsSubsetOf(CodePointSet other)
    {
        return Except(other).IsEmpty;
    }

    /// <summary>
    /// Collects ranges in any order, and builds the set they cover.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<(int First, int Last)> _ranges = new();

        public void Add(int first, int last)
        {
            first = Math.Max(first, 0);
            last = Math.Min(last, MaxCodePoint);
            if (first > last)
            {
                return;
            }

            if (first < FirstSurrogate
                && last > LastSurrogate)
            {
                _ranges.Add((first, FirstSurrogate - 1));
                _ranges.Add((LastSurrogate + 1, last));
                return;
            }

            if (first >= FirstSurrogate
                && first <= LastSurrogate)
            {
                first = LastSurrogate + 1;
            }

            if (last >= FirstSurrogate
                && last <= LastSurrogate)
            {
                last = FirstSurrogate - 1;
            }

            if (first <= last)
            {
                _ranges.Add((first, last));
            }
        }

        public void Add(CodePointSet set)
        {
            for (var index = 0; index < set.RangeCount; index++)
            {
                _ranges.Add((set.RangeFirst(index), set.RangeLast(index)));
            }
        }

        public CodePointSet ToSet()
        {
            if (_ranges.Count == 0)
            {
                return Empty;
            }

            _ranges.Sort();

            var bounds = new List<int>(_ranges.Count * 2);
            var currentFirst = _ranges[0].First;
            var currentLast = _ranges[0].Last;
            for (var index = 1; index < _ranges.Count; index++)
            {
                var range = _ranges[index];
                if (range.First <= currentLast + 1)
                {
                    currentLast = Math.Max(currentLast, range.Last);
                    continue;
                }

                bounds.Add(currentFirst);
                bounds.Add(currentLast);
                currentFirst = range.First;
                currentLast = range.Last;
            }
            bounds.Add(currentFirst);
            bounds.Add(currentLast);

            return new CodePointSet(bounds.ToArray());
        }
    }
}
