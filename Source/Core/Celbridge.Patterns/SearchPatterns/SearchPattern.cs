using System.Text;
using System.Text.RegularExpressions;

namespace Celbridge.Patterns;

/// <summary>
/// How a search pattern reads and matches. Literal reads the pattern as plain text. MatchCase matches letter case
/// exactly. WholeWord accepts a match only when no word character touches it. Multiline lets the pattern match across
/// lines.
/// </summary>
public sealed record SearchPatternOptions(bool Literal, bool MatchCase, bool WholeWord, bool Multiline);

/// <summary>
/// The start and length of a match or a group, in UTF-16 characters.
/// </summary>
internal readonly record struct SearchMatchSpan(int Start, int Length);

/// <summary>
/// A match of a search pattern. Start and Length count UTF-16 characters in the text searched.
/// </summary>
public sealed class SearchMatch
{
    internal SearchMatch(string text, int start, int length, IReadOnlyList<SearchMatchSpan?> groups)
    {
        Text = text;
        Start = start;
        Length = length;
        Groups = groups;
    }

    public int Start { get; }

    public int Length { get; }

    internal string Text { get; }

    // The match is group 0. A group that took no part in the match is null.
    internal IReadOnlyList<SearchMatchSpan?> Groups { get; }
}

/// <summary>
/// A search pattern in ripgrep's regex dialect, or plain text. A pattern matches the same text that ripgrep matches
/// with --crlf, in time linear in the length of the text.
/// </summary>
public sealed class SearchPattern
{
    private const string LineBreakMessage =
        "A pattern matches within one line, so it cannot match a line break. Use multiline to match across lines.";

    private const string CarriageReturnMessage =
        "With multiline, every line break reads as '\\n'. Write '\\n' for a line break, in place of '\\r\\n' or '\\r'.";

    private const string TooLargeMessage =
        "The pattern is too large to match in linear time. Make its repetition counts smaller, like '{100}'.";

    private const RegexOptions DotNetRegexOptions =
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private sealed record NormalizedText(string Text, int[]? Offsets);

    private readonly record struct Edges(CodePointSet First, CodePointSet Last, bool IsNullable);

    private readonly SearchPatternOptions _options;
    private readonly string? _literal;
    private readonly Regex? _regex;
    private readonly SupplementaryPartition? _partition;
    private readonly int _groupCount;
    private readonly IReadOnlyDictionary<string, int> _groupNumbers;

    private SearchPattern(
        SearchPatternOptions options,
        string? literal,
        Regex? regex,
        SupplementaryPartition? partition,
        int groupCount,
        IReadOnlyDictionary<string, int> groupNumbers)
    {
        _options = options;
        _literal = literal;
        _regex = regex;
        _partition = partition;
        _groupCount = groupCount;
        _groupNumbers = groupNumbers;
    }

    /// <summary>
    /// Compiles a pattern, or refuses one outside ripgrep's dialect with a message that says what to write.
    /// </summary>
    public static Result<SearchPattern> Compile(string pattern, SearchPatternOptions options)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return Result.Fail("The pattern is empty.");
        }

        if (options.Literal)
        {
            return CompileLiteral(pattern, options);
        }

        return CompileRegex(pattern, options);
    }

    /// <summary>
    /// Finds the matches in a text, in order. Matches never overlap. Without multiline, a line ends at '\n', '\r\n' or
    /// '\r', and each line is matched apart from the others.
    /// </summary>
    public IReadOnlyList<SearchMatch> FindMatches(string text)
    {
        List<SearchMatch>? matches = null;
        if (_options.Multiline)
        {
            var normalized = NormalizeLineBreaks(text);
            FindInSegment(text, normalized.Text, normalized.Offsets, 0, ref matches);
        }
        else
        {
            var segmentStart = 0;
            while (true)
            {
                var breakIndex = text.AsSpan(segmentStart).IndexOfAny('\r', '\n');
                if (breakIndex < 0)
                {
                    var lastSegment = Segment(text, segmentStart, text.Length);
                    FindInSegment(text, lastSegment, null, segmentStart, ref matches);
                    break;
                }

                breakIndex += segmentStart;
                var segment = Segment(text, segmentStart, breakIndex);
                FindInSegment(text, segment, null, segmentStart, ref matches);

                var nextStart = breakIndex + 1;
                if (text[breakIndex] == '\r'
                    && nextStart < text.Length
                    && text[nextStart] == '\n')
                {
                    nextStart++;
                }

                segmentStart = nextStart;
                if (segmentStart == text.Length)
                {
                    break;
                }
            }
        }

        if (matches is null)
        {
            return Array.Empty<SearchMatch>();
        }

        return matches;
    }

    /// <summary>
    /// Compiles a replacement against the pattern's groups. For a regex, the replacement follows ripgrep's --replace.
    /// For plain text, the replacement is plain text too.
    /// </summary>
    public Result<SearchReplacement> CompileReplacement(string replacement)
    {
        return SearchReplacement.Compile(replacement, _options.Literal, _groupCount, _groupNumbers);
    }

    private static Result<SearchPattern> CompileLiteral(string pattern, SearchPatternOptions options)
    {
        if (HasLoneSurrogate(pattern))
        {
            return Result.Fail("The pattern contains half of a surrogate pair, which is not a character.");
        }

        var literal = pattern;
        if (options.Multiline)
        {
            literal = NormalizeLineBreaks(literal).Text;
        }
        else if (literal.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return Result.Fail(LineBreakMessage);
        }

        if (options.MatchCase)
        {
            var emptyGroupNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            return new SearchPattern(options, literal, regex: null, partition: null, groupCount: 0, emptyGroupNumbers);
        }

        var items = new List<RegexNode>();
        foreach (var rune in literal.EnumerateRunes())
        {
            var codePointSet = CodePointSet.FromCodePoint(rune.Value);
            var foldedSet = UnicodeClasses.CaseFold(codePointSet);
            items.Add(new CharacterSetNode(foldedSet));
        }

        var root = new SequenceNode(items);
        var groupNumbers = new Dictionary<string, int>(StringComparer.Ordinal);

        return Build(root, options, groupCount: 0, groupNumbers);
    }

    private static Result<SearchPattern> CompileRegex(string pattern, SearchPatternOptions options)
    {
        var parseResult = RipgrepRegexParser.Parse(pattern, ignoreCase: !options.MatchCase);
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult.FirstErrorMessage);
        }
        var parsed = parseResult.Value;

        var lineResult = RemoveLineBreaks(parsed.Root, options.Multiline);
        if (lineResult.IsFailure)
        {
            return Result.Fail(lineResult.FirstErrorMessage);
        }
        var root = lineResult.Value;

        if (options.WholeWord)
        {
            var edgeResult = CheckWordEdges(root, pattern);
            if (edgeResult.IsFailure)
            {
                return Result.Fail(edgeResult.FirstErrorMessage);
            }

            var boundary = new AssertionNode(RegexAssertion.WordBoundary);
            var bounded = new List<RegexNode>
            {
                boundary,
                root,
                boundary,
            };
            root = new SequenceNode(bounded);
        }

        return Build(root, options, parsed.GroupCount, parsed.GroupNumbers);
    }

    private static Result<SearchPattern> Build(
        RegexNode root,
        SearchPatternOptions options,
        int groupCount,
        IReadOnlyDictionary<string, int> groupNumbers)
    {
        var writeResult = DotNetRegexWriter.Write(root);
        if (writeResult.IsFailure)
        {
            return Result.Fail(writeResult.FirstErrorMessage);
        }
        var written = writeResult.Value;

        Regex regex;
        try
        {
            regex = new Regex(written.Pattern, DotNetRegexOptions);
        }
        catch (NotSupportedException)
        {
            return Result.Fail(TooLargeMessage);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail($"The pattern could not be compiled: {exception.Message}");
        }

        return new SearchPattern(options, literal: null, regex, written.Partition, groupCount, groupNumbers);
    }

    // A line contains no line break, so ripgrep refuses a set of line breaks alone. With multiline, the text reads
    // every line break as '\n', so a set of '\r' alone is refused too.
    private static Result<RegexNode> RemoveLineBreaks(RegexNode root, bool isMultiline)
    {
        CodePointSet removed;
        string message;
        if (isMultiline)
        {
            removed = CodePointSet.FromCodePoint('\r');
            message = CarriageReturnMessage;
        }
        else
        {
            removed = CodePointSet.FromRangePairs(
            [
                '\n', '\n',
                '\r', '\r',
            ]);
            message = LineBreakMessage;
        }

        var isRefused = false;
        RegexNode Rewrite(RegexNode node)
        {
            switch (node)
            {
                case CharacterSetNode setNode:
                    if (!setNode.Set.IsEmpty
                        && setNode.Set.IsSubsetOf(removed))
                    {
                        isRefused = true;
                    }
                    return new CharacterSetNode(setNode.Set.Except(removed));

                case SequenceNode sequence:
                    var items = sequence.Items
                        .Select(Rewrite)
                        .ToList();
                    return new SequenceNode(items);

                case AlternationNode alternation:
                    var branches = alternation.Branches
                        .Select(Rewrite)
                        .ToList();
                    return new AlternationNode(branches);

                case RepetitionNode repetition:
                    return repetition with { Item = Rewrite(repetition.Item) };

                case CaptureNode capture:
                    return capture with { Item = Rewrite(capture.Item) };

                default:
                    return node;
            }
        }

        var rewritten = Rewrite(root);
        if (isRefused)
        {
            return Result.Fail(message);
        }

        return rewritten;
    }

    // '\b' gives ripgrep's whole-word answer when every match starts and ends with a word character.
    private static Result CheckWordEdges(RegexNode root, string pattern)
    {
        var edges = EdgesOf(root);
        if (edges.IsNullable)
        {
            return Result.Fail($"wholeWord needs a regex whose matches start and end with a word character, and '{pattern}' can match empty text. Remove wholeWord, or set literal for plain text.");
        }

        if (!edges.First.IsSubsetOf(UnicodeClasses.Word)
            || !edges.Last.IsSubsetOf(UnicodeClasses.Word))
        {
            return Result.Fail($"wholeWord needs a regex whose matches start and end with a word character, and '{pattern}' can start or end with another character. Remove wholeWord and write '\\b' where a word starts or ends, or set literal for plain text.");
        }

        return Result.Ok();
    }

    private static Edges EdgesOf(RegexNode node)
    {
        switch (node)
        {
            case CharacterSetNode setNode:
                return new Edges(setNode.Set, setNode.Set, IsNullable: false);

            case CaptureNode capture:
                return EdgesOf(capture.Item);

            case RepetitionNode repetition:
                if (repetition.Max == 0)
                {
                    return new Edges(CodePointSet.Empty, CodePointSet.Empty, IsNullable: true);
                }

                var itemEdges = EdgesOf(repetition.Item);
                return itemEdges with { IsNullable = itemEdges.IsNullable || repetition.Min == 0 };

            case AlternationNode alternation:
                var first = CodePointSet.Empty;
                var last = CodePointSet.Empty;
                var isNullable = false;
                foreach (var branch in alternation.Branches)
                {
                    var branchEdges = EdgesOf(branch);
                    first = first.Union(branchEdges.First);
                    last = last.Union(branchEdges.Last);
                    isNullable = isNullable || branchEdges.IsNullable;
                }
                return new Edges(first, last, isNullable);

            case SequenceNode sequence:
                var itemsEdges = sequence.Items
                    .Select(EdgesOf)
                    .ToList();

                var sequenceFirst = CodePointSet.Empty;
                foreach (var edges in itemsEdges)
                {
                    sequenceFirst = sequenceFirst.Union(edges.First);
                    if (!edges.IsNullable)
                    {
                        break;
                    }
                }

                var sequenceLast = CodePointSet.Empty;
                for (var index = itemsEdges.Count - 1; index >= 0; index--)
                {
                    sequenceLast = sequenceLast.Union(itemsEdges[index].Last);
                    if (!itemsEdges[index].IsNullable)
                    {
                        break;
                    }
                }

                var isSequenceNullable = itemsEdges.All(edges => edges.IsNullable);
                return new Edges(sequenceFirst, sequenceLast, isSequenceNullable);

            default:
                return new Edges(CodePointSet.Empty, CodePointSet.Empty, IsNullable: true);
        }
    }

    // Finds the matches in one segment of the text. Offsets map each index in the segment to its index in the text,
    // or the segment starts at segmentStart in the text. The search steps past an empty match at the end of the
    // previous match, the same as ripgrep. A match never starts between the halves of a surrogate pair.
    private void FindInSegment(string text, string segment, int[]? offsets, int segmentStart, ref List<SearchMatch>? matches)
    {
        var regexText = segment;
        if (_partition is not null)
        {
            regexText = _partition.Encode(segment);
        }

        var position = 0;
        var lastMatchEnd = -1;
        while (position <= segment.Length)
        {
            int start;
            int length;
            Match? regexMatch = null;
            if (_literal is not null)
            {
                start = segment.IndexOf(_literal, position, StringComparison.Ordinal);
                if (start < 0)
                {
                    break;
                }
                length = _literal.Length;
            }
            else
            {
                regexMatch = _regex!.Match(regexText, position);
                if (!regexMatch.Success)
                {
                    break;
                }
                start = regexMatch.Index;
                length = regexMatch.Length;
            }

            if (length == 0)
            {
                if (start == lastMatchEnd
                    || IsInsideSurrogatePair(segment, start))
                {
                    position = NextCharacter(segment, start);
                    continue;
                }
            }
            else if (_options.Literal
                && _options.WholeWord
                && !HasWordEdges(segment, start, length))
            {
                position = NextCharacter(segment, start);
                continue;
            }

            matches ??= new List<SearchMatch>();
            var match = CreateMatch(text, offsets, segmentStart, start, length, regexMatch);
            matches.Add(match);

            lastMatchEnd = start + length;
            if (length == 0)
            {
                position = NextCharacter(segment, start);
            }
            else
            {
                position = start + length;
            }
        }
    }

    private SearchMatch CreateMatch(string text, int[]? offsets, int segmentStart, int start, int length, Match? regexMatch)
    {
        SearchMatchSpan MapSpan(int spanStart, int spanLength)
        {
            int MapIndex(int index)
            {
                if (offsets is null)
                {
                    return segmentStart + index;
                }

                return offsets[index];
            }

            var mappedStart = MapIndex(spanStart);
            var mappedEnd = MapIndex(spanStart + spanLength);

            return new SearchMatchSpan(mappedStart, mappedEnd - mappedStart);
        }

        var whole = MapSpan(start, length);
        var groups = new SearchMatchSpan?[_groupCount + 1];
        groups[0] = whole;
        if (regexMatch is not null)
        {
            for (var number = 1; number <= _groupCount; number++)
            {
                var group = regexMatch.Groups[number];
                if (group.Success)
                {
                    groups[number] = MapSpan(group.Index, group.Length);
                }
            }
        }

        return new SearchMatch(text, whole.Start, whole.Length, groups);
    }

    private static string Segment(string text, int start, int end)
    {
        if (start == 0
            && end == text.Length)
        {
            return text;
        }

        return text.Substring(start, end - start);
    }

    // Reads each '\r\n' and each '\r' as '\n'. Offsets maps each index of the new text, and its end, to the original.
    private static NormalizedText NormalizeLineBreaks(string text)
    {
        if (text.IndexOf('\r') < 0)
        {
            return new NormalizedText(text, null);
        }

        var builder = new StringBuilder(text.Length);
        var offsets = new int[text.Length + 1];
        for (var index = 0; index < text.Length; index++)
        {
            offsets[builder.Length] = index;
            var character = text[index];
            if (character == '\r')
            {
                builder.Append('\n');
                if (index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                builder.Append(character);
            }
        }
        offsets[builder.Length] = text.Length;

        return new NormalizedText(builder.ToString(), offsets);
    }

    private static bool HasWordEdges(string text, int start, int length)
    {
        var before = CodePointBefore(text, start);
        var after = CodePointAt(text, start + length);

        return !IsWordCodePoint(before)
            && !IsWordCodePoint(after);
    }

    private static bool IsWordCodePoint(int codePoint)
    {
        return codePoint >= 0
            && UnicodeClasses.Word.Contains(codePoint);
    }

    private static int CodePointBefore(string text, int index)
    {
        if (index == 0)
        {
            return -1;
        }

        var character = text[index - 1];
        if (char.IsLowSurrogate(character)
            && index >= 2
            && char.IsHighSurrogate(text[index - 2]))
        {
            return char.ConvertToUtf32(text[index - 2], character);
        }

        return character;
    }

    private static int CodePointAt(string text, int index)
    {
        if (index >= text.Length)
        {
            return -1;
        }

        var character = text[index];
        if (char.IsHighSurrogate(character)
            && index + 1 < text.Length
            && char.IsLowSurrogate(text[index + 1]))
        {
            return char.ConvertToUtf32(character, text[index + 1]);
        }

        return character;
    }

    private static bool IsInsideSurrogatePair(string text, int index)
    {
        return index > 0
            && index < text.Length
            && char.IsLowSurrogate(text[index])
            && char.IsHighSurrogate(text[index - 1]);
    }

    private static int NextCharacter(string text, int index)
    {
        if (index < text.Length
            && char.IsHighSurrogate(text[index])
            && index + 1 < text.Length
            && char.IsLowSurrogate(text[index + 1]))
        {
            return index + 2;
        }

        return index + 1;
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character)
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
                continue;
            }

            if (char.IsSurrogate(character))
            {
                return true;
            }
        }

        return false;
    }
}
