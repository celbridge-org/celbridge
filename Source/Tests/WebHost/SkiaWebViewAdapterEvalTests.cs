using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// On macOS the page encodes an evaluated value as JSON and returns it as the only string in an array. These
/// tests pin the script that does this and the decoding of its result.
/// </summary>
[TestFixture]
public class SkiaWebViewAdapterEvalTests
{
    [Test]
    public void AnExpression_IsEncodedByThePage()
    {
        SkiaWebViewAdapter.BuildPageEncodedScript("document.title")
            .Should().Be("[JSON.stringify((\ndocument.title\n)) ?? null]");
    }

    [Test]
    public void ATrailingSemicolon_IsLeftOutOfTheParentheses()
    {
        SkiaWebViewAdapter.BuildPageEncodedScript("document.title; ")
            .Should().Be("[JSON.stringify((\ndocument.title\n)) ?? null]");
    }

    [Test]
    public void ATrailingLineComment_EndsBeforeTheClosingParentheses()
    {
        SkiaWebViewAdapter.BuildPageEncodedScript("1 + 1 // two")
            .Should().Be("[JSON.stringify((\n1 + 1 // two\n)) ?? null]");
    }

    [Test]
    public void TheEncodedString_DecodesToTheValuesJson()
    {
        // A value holding a quote and a backslash, as NSJSONSerialization escapes it inside the array.
        var returned = """["{\"text\":\"a \\\"quoted\\\" \\\\ name\"}"]""";

        SkiaWebViewAdapter.DecodePageEncodedResult(returned)
            .Should().Be("""{"text":"a \"quoted\" \\ name"}""");
    }

    [Test]
    public void AValueJsonCannotRepresent_DecodesToNull()
    {
        SkiaWebViewAdapter.DecodePageEncodedResult("[null]").Should().Be("null");
    }

    [Test]
    public void NoResult_DecodesToNull()
    {
        SkiaWebViewAdapter.DecodePageEncodedResult(null).Should().Be("null");
        SkiaWebViewAdapter.DecodePageEncodedResult("null").Should().Be("null");
    }
}
