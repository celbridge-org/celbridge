using System.Diagnostics;

namespace Celbridge.Tests.Patterns;

/// <summary>
/// Covers refusals and their messages, line handling and replacements. SearchPatternRipgrepComparisonTests covers how
/// patterns match.
/// </summary>
[TestFixture]
public class SearchPatternTests
{
    private static readonly SearchPatternOptions Regex = new(Literal: false, MatchCase: true, WholeWord: false, Multiline: false);

    private static readonly SearchPatternOptions Literal = new(Literal: true, MatchCase: true, WholeWord: false, Multiline: false);

    private static SearchPattern Compile(string pattern, SearchPatternOptions options)
    {
        var compileResult = SearchPattern.Compile(pattern, options);
        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);

        return compileResult.Value;
    }

    private static string CompileError(string pattern, SearchPatternOptions options)
    {
        var compileResult = SearchPattern.Compile(pattern, options);
        compileResult.IsFailure.Should().BeTrue();

        return compileResult.FirstErrorMessage;
    }

    private static List<string> MatchedText(SearchPattern pattern, string text)
    {
        return pattern
            .FindMatches(text)
            .Select(match => text.Substring(match.Start, match.Length))
            .ToList();
    }

    [Test]
    public void EmptyPattern_IsRefused()
    {
        CompileError(string.Empty, Regex).Should().Contain("empty");
    }

    [TestCase("a(?=b)", "Lookaround")]
    [TestCase("(?<!a)b", "Lookaround")]
    [TestCase(@"(a)\1", "Backreferences")]
    [TestCase(@"\p{Greek}", "general category")]
    [TestCase(@"\<foo", @"'\b'")]
    [TestCase(@"\Afoo", "'^'")]
    [TestCase(@"foo\z", "'$'")]
    [TestCase("foo{bar", @"'\{'")]
    [TestCase("(?-m)^a", "'^' and '$'")]
    [TestCase("[abc", @"'\['")]
    [TestCase("(abc", @"'\('")]
    public void RefusedConstruct_SaysWhatToWrite(string pattern, string advice)
    {
        CompileError(pattern, Regex).Should().Contain(advice);
    }

    [TestCase(@"a\nb")]
    [TestCase("a\nb")]
    public void LineBreakWithoutMultiline_IsRefused(string pattern)
    {
        CompileError(pattern, Regex).Should().Contain("multiline");
    }

    [Test]
    public void LiteralLineBreakWithoutMultiline_IsRefused()
    {
        CompileError("a\nb", Literal).Should().Contain("multiline");
    }

    [Test]
    public void CarriageReturnWithMultiline_IsRefused()
    {
        var multiline = Regex with { Multiline = true };

        CompileError(@"a\r\nb", multiline).Should().Contain(@"'\n'");
    }

    [Test]
    public void WholeWordOnARegexThatCanStartWithPunctuation_IsRefused()
    {
        var wholeWord = Regex with { WholeWord = true };

        CompileError("-foo", wholeWord).Should().Contain("wholeWord");
    }

    [Test]
    public void WholeWordOnARegexThatCanMatchEmptyText_IsRefused()
    {
        var wholeWord = Regex with { WholeWord = true };

        CompileError("a*", wholeWord).Should().Contain("empty text");
    }

    [Test]
    public void HugeRepetition_IsRefused()
    {
        CompileError("a{100000}", Regex).Should().Contain("too large");
    }

    [Test]
    public void LinesEndingInEachWay_AreMatchedApart()
    {
        var pattern = Compile("^.+$", Regex);

        MatchedText(pattern, "one\ntwo\r\nthree\rfour").Should().Equal("one", "two", "three", "four");
    }

    [Test]
    public void LineBreakAtTheEnd_EndsTheLastLine()
    {
        var pattern = Compile("^$", Regex);

        pattern.FindMatches("a\n").Should().BeEmpty();
        pattern.FindMatches("a\n\n").Should().ContainSingle();
    }

    [Test]
    public void Multiline_MatchesAcrossLines_WithOffsetsInTheOriginalText()
    {
        var multiline = Regex with { Multiline = true };
        var pattern = Compile(@"one\ntwo", multiline);
        var text = "zero\r\none\r\ntwo\r\n";

        var match = pattern.FindMatches(text).Single();

        text.Substring(match.Start, match.Length).Should().Be("one\r\ntwo");
    }

    [Test]
    public void Replacement_TakesGroupTextFromTheOriginalText()
    {
        var multiline = Regex with { Multiline = true };
        var pattern = Compile(@"(a\nb)", multiline);
        var replacement = pattern.CompileReplacement("[$1]").Value;
        var text = "a\r\nb\r\n";

        var match = pattern.FindMatches(text).Single();

        replacement.Expand(match).Should().Be("[a\r\nb]");
    }

    [Test]
    public void LiteralReplacement_IsPlainText()
    {
        var pattern = Compile("price", Literal);
        var replacement = pattern.CompileReplacement("$1 and ${name}").Value;
        var match = pattern.FindMatches("price").Single();

        replacement.Expand(match).Should().Be("$1 and ${name}");
    }

    [Test]
    public void ReplacementNamingAGroupFollowedByLetters_SuggestsBraces()
    {
        var pattern = Compile("(a)", Regex);

        var replacementResult = pattern.CompileReplacement("$1st");

        replacementResult.IsFailure.Should().BeTrue();
        replacementResult.FirstErrorMessage.Should().Contain("${1}st");
    }

    [Test]
    public void ReplacementNamingAMissingGroup_IsRefused()
    {
        var pattern = Compile("(?<word>a)", Regex);

        pattern.CompileReplacement("$word").IsSuccess.Should().BeTrue();
        pattern.CompileReplacement("$other").IsFailure.Should().BeTrue();
    }

    [Test]
    public void GroupNumbers_FollowTheOpeningParentheses()
    {
        var pattern = Compile("(?<first>a)(b)", Regex);
        var replacement = pattern.CompileReplacement("$2$1").Value;
        var match = pattern.FindMatches("ab").Single();

        replacement.Expand(match).Should().Be("ba");
    }

    [Test]
    public void NestedQuantifiers_MatchInLinearTime()
    {
        // A backtracking engine takes seconds on this line, and each extra 'a' doubles the time.
        var pattern = Compile("(a+)+$", Regex);
        var line = new string('a', 5000) + "!";

        var matchTask = Task.Run(() => pattern.FindMatches(line));

        matchTask.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("the match should finish within 2 seconds");
        matchTask.Result.Should().BeEmpty();
    }

    [Test]
    public void WordClass_CompilesQuickly()
    {
        Compile(@"\w", Regex);

        var stopwatch = Stopwatch.StartNew();
        Compile(@"\b\w+\b|[\p{L}\d_]+|\W+", Regex);
        stopwatch.Stop();

        TestContext.Out.WriteLine($"Compiled in {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
