using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the reading of the content probe a hosted page answers with. The text comes back through
/// script evaluation, whose encoding differs per head, and the verdict decides whether a page is reported as
/// loaded blank, so the shapes, the edge cases and the handling of page-authored text are pinned here.
/// </summary>
[TestFixture]
public class WebViewLoadDiagnosticsTests
{
    private const string EmptyDocument =
        """{"url":"https://www.example.org/","readyState":"complete","htmlLength":39,"headChildCount":0,"bodyChildCount":0}""";

    private const string LoadedDocument =
        """{"url":"https://www.example.org/","readyState":"complete","htmlLength":-1,"headChildCount":91,"bodyChildCount":8}""";

    // Windows encodes the script's string result as a JSON string literal; the macOS head returns it bare.
    private static string Quoted(string json) => System.Text.Json.JsonSerializer.Serialize(json);

    private static string Document(string url, string readyState, int htmlLength, int headChildCount, int bodyChildCount)
    {
        return $$"""
            {"url":"{{url}}","readyState":"{{readyState}}","htmlLength":{{htmlLength}},"headChildCount":{{headChildCount}},"bodyChildCount":{{bodyChildCount}}}
            """;
    }

    [Test]
    public void EmptyDocument_IsEmpty_WhetherQuotedOrBare()
    {
        WebViewLoadDiagnostics.ReadContentProbe(Quoted(EmptyDocument), out var fromQuoted).Should().Be(ProbeOutcome.Read);
        fromQuoted.IsEmpty.Should().BeTrue();

        WebViewLoadDiagnostics.ReadContentProbe(EmptyDocument, out var fromBare).Should().Be(ProbeOutcome.Read);
        fromBare.IsEmpty.Should().BeTrue();

        fromQuoted.Reading.Should().Be(fromBare.Reading);
    }

    [Test]
    public void LoadedDocument_IsNotEmpty()
    {
        WebViewLoadDiagnostics.ReadContentProbe(Quoted(LoadedDocument), out var probe).Should().Be(ProbeOutcome.Read);
        probe.IsEmpty.Should().BeFalse();
    }

    // The page skips measuring a document that has elements, so an unmeasured length is never emptiness.
    [Test]
    public void UnmeasuredLength_IsNotEmpty()
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            Document("https://www.example.org/", "complete", htmlLength: -1, headChildCount: 0, bodyChildCount: 0),
            out var probe).Should().Be(ProbeOutcome.Read);

        probe.IsEmpty.Should().BeFalse();
    }

    [TestCase(128, true)]
    [TestCase(129, false)]
    public void HtmlLength_IsEmptyOnlyUpToTheThreshold(int htmlLength, bool expected)
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            Document("https://www.example.org/", "complete", htmlLength, headChildCount: 0, bodyChildCount: 0),
            out var probe).Should().Be(ProbeOutcome.Read);

        probe.IsEmpty.Should().Be(expected);
    }

    // The blank page a load starts from says nothing about a response, so it is never looked at again.
    [Test]
    public void BlankDocument_GivesNoVerdict()
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            Document("about:blank", "complete", htmlLength: 39, headChildCount: 0, bodyChildCount: 0),
            out _).Should().Be(ProbeOutcome.NoVerdict);
    }

    // A document that has not settled is worth a second look rather than a verdict, which is what makes the
    // caller probe it once more.
    [TestCase("loading")]
    [TestCase("interactive")]
    public void UnsettledDocument_IsStillParsing(string readyState)
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            Document("https://www.example.org/", readyState, htmlLength: 39, headChildCount: 0, bodyChildCount: 0),
            out _).Should().Be(ProbeOutcome.StillParsing);
    }

    [Test]
    public void UnreadableResult_GivesNoVerdict()
    {
        WebViewLoadDiagnostics.ReadContentProbe(null, out _).Should().Be(ProbeOutcome.NoVerdict);
        WebViewLoadDiagnostics.ReadContentProbe("null", out _).Should().Be(ProbeOutcome.NoVerdict);
        WebViewLoadDiagnostics.ReadContentProbe("not json", out _).Should().Be(ProbeOutcome.NoVerdict);
        WebViewLoadDiagnostics.ReadContentProbe(Quoted("not json"), out _).Should().Be(ProbeOutcome.NoVerdict);
        WebViewLoadDiagnostics.ReadContentProbe("""{"readyState":"complete"}""", out _).Should().Be(ProbeOutcome.NoVerdict);
    }

    // A page chooses the numbers it reports, so a non-numeric count is a report the host cannot read rather
    // than a document it should judge.
    [Test]
    public void NonNumericCounts_GiveNoVerdict()
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            """{"url":"https://www.example.org/","readyState":"complete","htmlLength":"39","headChildCount":0,"bodyChildCount":0}""",
            out _).Should().Be(ProbeOutcome.NoVerdict);
    }

    // Nothing the page authored reaches the log unbounded, and a page cannot forge log entries by embedding
    // line breaks in the text it reports.
    [Test]
    public void PageAuthoredText_IsCappedAndSingleLine()
    {
        var url = "https://www.example.org/" + new string('x', 500);

        WebViewLoadDiagnostics.ReadContentProbe(
            $$"""{"url":"{{url}}","readyState":"complete","htmlLength":39,"headChildCount":0,"bodyChildCount":0}""",
            out var probe).Should().Be(ProbeOutcome.Read);

        probe.Reading.Should().Contain("...");
        probe.Reading.Length.Should().BeLessThan(400);
    }

    [Test]
    public void PageAuthoredNewlines_AreStripped()
    {
        WebViewLoadDiagnostics.ReadContentProbe(
            """{"url":"https://www.example.org/\n2026-01-01 [WARN] forged entry","readyState":"complete","htmlLength":39,"headChildCount":0,"bodyChildCount":0}""",
            out var probe).Should().Be(ProbeOutcome.Read);

        probe.Reading.Should().NotContain("\n");
        probe.Reading.Should().NotContain("\r");
    }

    // Absent optional fields are reported as unknown rather than dropping the verdict, so an older page or a
    // head without navigation timing still produces a reading.
    [Test]
    public void AbsentOptionalFields_ReadAsUnknown()
    {
        WebViewLoadDiagnostics.ReadContentProbe(EmptyDocument, out var probe).Should().Be(ProbeOutcome.Read);

        probe.Reading.Should().Contain("transfer=-1");
        probe.Reading.Should().Contain("html=39");
    }
}
