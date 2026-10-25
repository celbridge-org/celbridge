using System.Text;

namespace Celbridge.Patterns;

/// <summary>
/// A .NET regex written from a parsed regex, and the partition its text needs before matching.
/// </summary>
internal sealed record WrittenRegex(string Pattern, SupplementaryPartition? Partition);

/// <summary>
/// Writes a parsed regex as a .NET regex for RegexOptions.NonBacktracking. Each set matches a whole code point, so a
/// match never splits a surrogate pair. Every group gets an explicit number, so the numbers follow the order of the
/// opening parentheses.
/// </summary>
internal static class DotNetRegexWriter
{
    private const int MaxGroups = 1024;

    private const string AnyLowSurrogate = "[\\uDC00-\\uDFFF]";

    private const string MatchesNothing = "[^\\u0000-\\uFFFF]";

    private sealed class WriteState
    {
        public required SupplementaryPartition? Partition { get; init; }

        // The groups of the code points above U+FFFF that each set contains, by set.
        public required Dictionary<CodePointSet, int[]> SetGroups { get; init; }
    }

    public static Result<WrittenRegex> Write(RegexNode root)
    {
        var uniqueSets = new HashSet<CodePointSet>(ReferenceEqualityComparer.Instance);
        CollectSets(root, uniqueSets);
        var sets = uniqueSets.ToList();

        var partitionResult = BuildPartition(sets);
        if (partitionResult.IsFailure)
        {
            return Result.Fail(partitionResult.FirstErrorMessage);
        }
        var state = partitionResult.Value;

        var pattern = new StringBuilder();
        WriteNode(root, state, pattern);

        return new WrittenRegex(pattern.ToString(), state.Partition);
    }

    private static void CollectSets(RegexNode node, HashSet<CodePointSet> sets)
    {
        switch (node)
        {
            case CharacterSetNode setNode:
                sets.Add(setNode.Set);
                break;

            case SequenceNode sequence:
                foreach (var item in sequence.Items)
                {
                    CollectSets(item, sets);
                }
                break;

            case AlternationNode alternation:
                foreach (var branch in alternation.Branches)
                {
                    CollectSets(branch, sets);
                }
                break;

            case RepetitionNode repetition:
                CollectSets(repetition.Item, sets);
                break;

            case CaptureNode capture:
                CollectSets(capture.Item, sets);
                break;
        }
    }

    private static Result<WriteState> BuildPartition(List<CodePointSet> sets)
    {
        var boundaries = new SortedSet<int>
        {
            CodePointSet.FirstSupplementary,
        };
        foreach (var set in sets)
        {
            for (var index = 0; index < set.RangeCount; index++)
            {
                var first = set.RangeFirst(index);
                var last = set.RangeLast(index);
                if (last < CodePointSet.FirstSupplementary)
                {
                    continue;
                }

                boundaries.Add(Math.Max(first, CodePointSet.FirstSupplementary));
                if (last < CodePointSet.MaxCodePoint)
                {
                    boundaries.Add(last + 1);
                }
            }
        }

        var intervalStarts = boundaries.ToArray();
        var intervalGroups = new int[intervalStarts.Length];
        var groupsBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
        var signatureSets = new List<List<int>>();
        for (var intervalIndex = 0; intervalIndex < intervalStarts.Length; intervalIndex++)
        {
            var containingSets = new List<int>();
            for (var setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                if (sets[setIndex].Contains(intervalStarts[intervalIndex]))
                {
                    containingSets.Add(setIndex);
                }
            }

            var signature = string.Join(",", containingSets);
            if (!groupsBySignature.TryGetValue(signature, out var group))
            {
                group = groupsBySignature.Count;
                groupsBySignature[signature] = group;
                signatureSets.Add(containingSets);
            }
            intervalGroups[intervalIndex] = group;
        }

        if (groupsBySignature.Count > MaxGroups)
        {
            return Result.Fail("The pattern's classes are too complex to match characters above U+FFFF. Use fewer classes.");
        }

        var groupLists = new List<int>[sets.Count];
        for (var setIndex = 0; setIndex < sets.Count; setIndex++)
        {
            groupLists[setIndex] = new List<int>();
        }
        for (var group = 0; group < signatureSets.Count; group++)
        {
            foreach (var setIndex in signatureSets[group])
            {
                groupLists[setIndex].Add(group);
            }
        }

        // With one group, each set contains all the code points above U+FFFF or excludes them all. The text then keeps
        // its own high surrogates.
        SupplementaryPartition? partition = null;
        if (groupsBySignature.Count > 1)
        {
            partition = new SupplementaryPartition(intervalStarts, intervalGroups);
        }

        var setGroups = new Dictionary<CodePointSet, int[]>(ReferenceEqualityComparer.Instance);
        for (var setIndex = 0; setIndex < sets.Count; setIndex++)
        {
            setGroups[sets[setIndex]] = groupLists[setIndex].ToArray();
        }

        var state = new WriteState
        {
            Partition = partition,
            SetGroups = setGroups,
        };

        return state;
    }

    // Returns true when the text written is one UTF-16 unit wide, so a quantifier can follow it directly.
    private static bool WriteNode(RegexNode node, WriteState state, StringBuilder pattern)
    {
        switch (node)
        {
            case CharacterSetNode setNode:
                return WriteSet(setNode.Set, state, pattern);

            case SequenceNode sequence:
                foreach (var item in sequence.Items)
                {
                    WriteNode(item, state, pattern);
                }
                return false;

            case AlternationNode alternation:
                pattern.Append("(?:");
                for (var index = 0; index < alternation.Branches.Count; index++)
                {
                    if (index > 0)
                    {
                        pattern.Append('|');
                    }
                    WriteNode(alternation.Branches[index], state, pattern);
                }
                pattern.Append(')');
                return false;

            case RepetitionNode repetition:
                WriteRepetition(repetition, state, pattern);
                return false;

            case CaptureNode capture:
                pattern.Append("(?<");
                pattern.Append(capture.Number);
                pattern.Append('>');
                WriteNode(capture.Item, state, pattern);
                pattern.Append(')');
                return false;

            case AssertionNode assertion:
                var assertionText = assertion.Assertion switch
                {
                    RegexAssertion.LineStart => "^",
                    RegexAssertion.LineEnd => "$",
                    RegexAssertion.WordBoundary => "\\b",
                    _ => "\\B",
                };
                pattern.Append(assertionText);
                return false;
        }

        throw new InvalidOperationException($"Unknown regex node: {node.GetType().Name}");
    }

    private static void WriteRepetition(RepetitionNode repetition, WriteState state, StringBuilder pattern)
    {
        var item = new StringBuilder();
        var isOneUnit = WriteNode(repetition.Item, state, item);
        if (isOneUnit)
        {
            pattern.Append(item);
        }
        else
        {
            pattern.Append("(?:");
            pattern.Append(item);
            pattern.Append(')');
        }

        var min = repetition.Min;
        var max = repetition.Max;
        if (min == 0
            && max is null)
        {
            pattern.Append('*');
        }
        else if (min == 1
            && max is null)
        {
            pattern.Append('+');
        }
        else if (min == 0
            && max == 1)
        {
            pattern.Append('?');
        }
        else if (max is null)
        {
            pattern.Append($"{{{min},}}");
        }
        else if (min == max)
        {
            pattern.Append($"{{{min}}}");
        }
        else
        {
            pattern.Append($"{{{min},{max}}}");
        }

        if (!repetition.IsGreedy)
        {
            pattern.Append('?');
        }
    }

    private static bool WriteSet(CodePointSet set, WriteState state, StringBuilder pattern)
    {
        var basicPlane = new StringBuilder();
        var basicRangeCount = 0;
        var basicSingle = -1;
        for (var index = 0; index < set.RangeCount; index++)
        {
            var first = set.RangeFirst(index);
            if (first >= CodePointSet.FirstSupplementary)
            {
                break;
            }

            var last = Math.Min(set.RangeLast(index), CodePointSet.FirstSupplementary - 1);
            AppendUnit(basicPlane, first);
            if (last > first)
            {
                basicPlane.Append('-');
                AppendUnit(basicPlane, last);
            }
            basicRangeCount++;
            basicSingle = first == last ? first : -1;
        }

        var supplementary = SupplementaryText(set, state);

        if (basicRangeCount == 0
            && supplementary is null)
        {
            pattern.Append(MatchesNothing);
            return true;
        }

        var basicText = string.Empty;
        if (basicRangeCount == 1
            && basicSingle >= 0)
        {
            var single = new StringBuilder();
            AppendUnit(single, basicSingle);
            basicText = single.ToString();
        }
        else if (basicRangeCount > 0)
        {
            basicText = $"[{basicPlane}]";
        }

        if (supplementary is null)
        {
            pattern.Append(basicText);
            return true;
        }

        if (basicRangeCount == 0)
        {
            pattern.Append(supplementary);
            return false;
        }

        pattern.Append("(?:");
        pattern.Append(basicText);
        pattern.Append('|');
        pattern.Append(supplementary);
        pattern.Append(')');

        return true;
    }

    // Returns the text that matches the set's code points above U+FFFF, or null when the set has none.
    private static string? SupplementaryText(CodePointSet set, WriteState state)
    {
        if (state.Partition is null)
        {
            if (!set.Contains(CodePointSet.FirstSupplementary))
            {
                return null;
            }

            return "[\\uD800-\\uDBFF]" + AnyLowSurrogate;
        }

        var groups = state.SetGroups[set];
        if (groups.Length == 0)
        {
            return null;
        }

        var markers = new StringBuilder("[");
        var index = 0;
        while (index < groups.Length)
        {
            var runEnd = index;
            while (runEnd + 1 < groups.Length
                && groups[runEnd + 1] == groups[runEnd] + 1)
            {
                runEnd++;
            }

            AppendUnit(markers, SupplementaryPartition.MarkerOf(groups[index]));
            if (runEnd > index)
            {
                markers.Append('-');
                AppendUnit(markers, SupplementaryPartition.MarkerOf(groups[runEnd]));
            }
            index = runEnd + 1;
        }
        markers.Append(']');

        return markers + AnyLowSurrogate;
    }

    private static void AppendUnit(StringBuilder pattern, int unit)
    {
        pattern.Append("\\u");
        pattern.Append(unit.ToString("X4"));
    }
}
