using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Pins how a host expression is wrapped, and how the wrapped script's result is unwrapped.
/// </summary>
[TestFixture]
public class ScriptValueJsonTests
{
    [Test]
    public void AnExpression_IsWrappedInJsonStringify()
    {
        ScriptValueJson.Wrap("document.title")
            .Should().Be("[JSON.stringify((\ndocument.title\n)) ?? null]");
    }

    [Test]
    public void ATrailingSemicolon_IsLeftOutOfTheParentheses()
    {
        ScriptValueJson.Wrap("document.title; ")
            .Should().Be("[JSON.stringify((\ndocument.title\n)) ?? null]");
    }

    [Test]
    public void ATrailingLineComment_EndsBeforeTheClosingParentheses()
    {
        ScriptValueJson.Wrap("1 + 1 // two")
            .Should().Be("[JSON.stringify((\n1 + 1 // two\n)) ?? null]");
    }

    [Test]
    public void TheArray_UnwrapsToThePagesJson()
    {
        // A value that contains a quote and a backslash, escaped inside the array.
        var returned = """["{\"text\":\"a \\\"quoted\\\" \\\\ name\"}"]""";

        ScriptValueJson.Unwrap(returned)
            .Should().Be("""{"text":"a \"quoted\" \\ name"}""");
    }

    [Test]
    public void AValueOutsideJson_UnwrapsToNull()
    {
        ScriptValueJson.Unwrap("[null]").Should().Be("null");
    }

    [Test]
    public void NoResult_UnwrapsToNull()
    {
        ScriptValueJson.Unwrap(null).Should().Be("null");
        ScriptValueJson.Unwrap("null").Should().Be("null");
    }
}
