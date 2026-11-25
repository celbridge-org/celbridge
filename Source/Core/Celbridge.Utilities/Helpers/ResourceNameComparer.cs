using System.Globalization;

namespace Celbridge.Utilities;

/// <summary>
/// Orders resource names for display. Letter case is ignored, and a run of digits compares by its numeric value, so
/// "v1.9" sorts before "v1.10". Names that differ only in case or leading zeros fall back to ordinal order, which is
/// the same on every platform.
/// </summary>
public sealed class ResourceNameComparer : IComparer<string>
{
    /// <summary>
    /// The shared instance of the comparer.
    /// </summary>
    public static ResourceNameComparer Instance { get; } = new();

    private static readonly CompareInfo TextCompareInfo = CultureInfo.InvariantCulture.CompareInfo;

    private ResourceNameComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }

        var xIndex = 0;
        var yIndex = 0;
        while (xIndex < x.Length
            && yIndex < y.Length)
        {
            var xRun = ReadRun(x, xIndex);
            var yRun = ReadRun(y, yIndex);

            int runComparison;
            if (IsDigitRun(xRun)
                && IsDigitRun(yRun))
            {
                runComparison = CompareNumbers(xRun, yRun);
            }
            else
            {
                runComparison = TextCompareInfo.Compare(xRun, yRun, CompareOptions.IgnoreCase);
            }

            if (runComparison != 0)
            {
                return runComparison;
            }

            xIndex += xRun.Length;
            yIndex += yRun.Length;
        }

        var xRemaining = x.Length - xIndex;
        var yRemaining = y.Length - yIndex;
        if (xRemaining != yRemaining)
        {
            return xRemaining.CompareTo(yRemaining);
        }

        return string.CompareOrdinal(x, y);
    }

    // Returns the run of digits, or of other characters, that starts at the index. A digit is any Unicode decimal
    // digit, such as the fullwidth digits of East Asian text.
    private static ReadOnlySpan<char> ReadRun(string text, int start)
    {
        var isDigit = char.IsDigit(text[start]);
        var end = start + 1;
        while (end < text.Length
            && char.IsDigit(text[end]) == isDigit)
        {
            end++;
        }

        return text.AsSpan(start, end - start);
    }

    private static bool IsDigitRun(ReadOnlySpan<char> run)
    {
        return char.IsDigit(run[0]);
    }

    // Compares two runs of digits by value. Any length of number is supported.
    private static int CompareNumbers(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var xDigits = TrimLeadingZeros(x);
        var yDigits = TrimLeadingZeros(y);
        if (xDigits.Length != yDigits.Length)
        {
            return xDigits.Length.CompareTo(yDigits.Length);
        }

        for (var index = 0; index < xDigits.Length; index++)
        {
            var digitComparison = DigitValue(xDigits[index]).CompareTo(DigitValue(yDigits[index]));
            if (digitComparison != 0)
            {
                return digitComparison;
            }
        }

        return 0;
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> digits)
    {
        var start = 0;
        while (start < digits.Length
            && DigitValue(digits[start]) == 0)
        {
            start++;
        }

        return digits.Slice(start);
    }

    private static int DigitValue(char digit)
    {
        return (int)char.GetNumericValue(digit);
    }
}
