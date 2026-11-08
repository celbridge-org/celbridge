using System.Text.Json;
using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// The packaged Windows head evaluates a host script through the DevTools protocol's Runtime.evaluate. The host's
/// script then doesn't change the page's user activation. The page encodes the value. These tests pin the head's
/// request and how its response reads.
/// </summary>
[TestFixture]
public class DevToolsEvaluationTests
{
    [Test]
    public void TheRequest_WrapsTheScript_WithUserGestureOff()
    {
        var script = "document.title + \" \\\"quoted\\\"\"";

        using var request = JsonDocument.Parse(DevToolsEvaluation.BuildEvaluateParams(script));
        var root = request.RootElement;

        root.GetProperty("expression").GetString().Should().Be(ScriptValueJson.Wrap(script));
        root.GetProperty("userGesture").GetBoolean().Should().BeFalse();
        root.GetProperty("returnByValue").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void TheEncodedValue_ReadsAsThePagesJson()
    {
        var response = """{"result":{"type":"object","subtype":"array","value":["{\"url\":\"http://a/\",\"count\":1}"]}}""";

        DevToolsEvaluation.ReadEvaluateResult(response).Should().Be("""{"url":"http://a/","count":1}""");
    }

    [Test]
    public void AValueOutsideJson_ReadsAsNull()
    {
        DevToolsEvaluation.ReadEvaluateResult("""{"result":{"type":"object","subtype":"array","value":[null]}}""")
            .Should().Be("null");
    }

    [Test]
    public void AThrownException_ReadsAsNull()
    {
        // JSON.stringify throws for an object that refers back to itself.
        var response = """
            {"result":{"type":"object","subtype":"error","className":"TypeError","description":"TypeError: circular"},
             "exceptionDetails":{"exceptionId":1,"text":"Uncaught","lineNumber":1,"columnNumber":16}}
            """;

        DevToolsEvaluation.ReadEvaluateResult(response).Should().Be("null");
    }

    [Test]
    public void NoResult_ReadsAsNull()
    {
        DevToolsEvaluation.ReadEvaluateResult("{}").Should().Be("null");
    }
}
