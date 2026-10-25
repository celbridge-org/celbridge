using System.Text;

namespace Celbridge.Tests.Patterns;

/// <summary>
/// Compares SearchPattern with ripgrep's recorded answers. SearchPatternComparisonRecorder records them with ripgrep.
/// Each row must give ripgrep's matches and replaced text, or be refused where ripgrep refuses the pattern.
/// </summary>
[TestFixture]
public class SearchPatternRipgrepComparisonTests
{
    private const string RecordAgain =
        "A row changed since ripgrep's answers were recorded. Run SearchPatternComparisonRecorder to record them again.";

    public static IEnumerable<string> RowNames()
    {
        return SearchPatternComparisonCases.Rows.Select(row => row.Name);
    }

    public static IEnumerable<string> DifferenceNames()
    {
        return SearchPatternComparisonCases.Differences.Select(row => row.Name);
    }

    [Test]
    public void RecordedAnswers_AreForTheCurrentRows()
    {
        SearchPatternComparisonAnswers.CasesFingerprint.Should().Be(SearchPatternComparisonCases.Fingerprint(), RecordAgain);
    }

    [Test]
    public void RowNames_AreUnique()
    {
        var names = RowNames().Concat(DifferenceNames());

        names.Should().OnlyHaveUniqueItems();
    }

    [TestCaseSource(nameof(RowNames))]
    public void Row_MatchesRipgrep(string name)
    {
        var row = SearchPatternComparisonCases.Rows.Single(row => row.Name == name);
        var answer = AnswerFor(name);

        var compileResult = SearchPattern.Compile(row.Pattern, row.Options);
        if (row.Refusal == SearchPatternRefusal.Pattern)
        {
            compileResult.IsFailure.Should().BeTrue("Celbridge refuses this pattern");
            return;
        }

        if (answer.IsRefused)
        {
            compileResult.IsFailure.Should().BeTrue("ripgrep refuses this pattern");
            return;
        }

        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);
        var pattern = compileResult.Value;

        var matches = pattern.FindMatches(row.Text);

        FormatMatches(matches).Should().Be(answer.Matches);

        if (row.Replacement is null)
        {
            return;
        }

        var replacementResult = pattern.CompileReplacement(row.Replacement);
        if (row.Refusal == SearchPatternRefusal.Replacement)
        {
            replacementResult.IsFailure.Should().BeTrue("Celbridge refuses this replacement");
            return;
        }

        replacementResult.IsSuccess.Should().BeTrue(replacementResult.FirstErrorMessage);
        var replacement = replacementResult.Value;

        Replace(row.Text, matches, replacement).Should().Be(answer.Replaced);
    }

    [TestCaseSource(nameof(DifferenceNames))]
    public void Difference_GivesCelbridgesAnswer(string name)
    {
        var row = SearchPatternComparisonCases.Differences.Single(row => row.Name == name);
        var answer = AnswerFor(name);

        var compileResult = SearchPattern.Compile(row.Pattern, row.Options);
        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);
        var pattern = compileResult.Value;

        var matches = pattern.FindMatches(row.Text);
        string? replaced = null;
        if (row.Replacement is not null)
        {
            var replacementResult = pattern.CompileReplacement(row.Replacement);
            replacementResult.IsSuccess.Should().BeTrue(replacementResult.FirstErrorMessage);
            replaced = Replace(row.Text, matches, replacementResult.Value);
        }

        FormatMatches(matches).Should().Be(row.ExpectedMatches);
        replaced.Should().Be(row.ExpectedReplaced);

        var ripgrepAgrees = !answer.IsRefused
            && answer.Matches == row.ExpectedMatches
            && answer.Replaced == row.ExpectedReplaced;
        ripgrepAgrees.Should().BeFalse("the row should show a difference from ripgrep");
    }

    private static SearchPatternAnswer AnswerFor(string name)
    {
        SearchPatternComparisonAnswers.Answers.TryGetValue(name, out var answer).Should().BeTrue(RecordAgain);

        return answer!;
    }

    private static string FormatMatches(IReadOnlyList<SearchMatch> matches)
    {
        var formatted = matches.Select(match => $"{match.Start}:{match.Length}");

        return string.Join(' ', formatted);
    }

    private static string Replace(string text, IReadOnlyList<SearchMatch> matches, SearchReplacement replacement)
    {
        var replaced = new StringBuilder();
        var position = 0;
        foreach (var match in matches)
        {
            replaced.Append(text, position, match.Start - position);
            replaced.Append(replacement.Expand(match));
            position = match.Start + match.Length;
        }
        replaced.Append(text, position, text.Length - position);

        return replaced.ToString();
    }
}
