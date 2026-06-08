using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Round-trip coverage for the TOML-only sidecar encoder. Every test composes
/// a fields value, parses it back, and asserts byte-equality between the
/// input value and the parsed value. Determinism and field ordering are
/// covered by the second group of tests; quote-style autoselection by the
/// first.
/// </summary>
[TestFixture]
public class SidecarRoundTripTests
{
    [Test]
    public void BareIdentifierValue_UsesBasicString_AndRoundTrips()
    {
        var value = "Sunset";

        AssertRoundTripsAsString("title", value);

        var composed = SidecarHelper.Compose(new Dictionary<string, object> { ["title"] = value });
        composed.Should().Contain("title = \"Sunset\"");
    }

    [Test]
    public void WindowsPathWithBackslashes_UsesLiteralTriple_AndRoundTrips()
    {
        var value = @"C:\Users\foo\notes.md";

        AssertRoundTripsAsString("path", value);

        var composed = SidecarHelper.Compose(new Dictionary<string, object> { ["path"] = value });
        composed.Should().Contain("'''");
        composed.Should().NotContain("\\\\");
    }

    [Test]
    public void MultiLineJson_UsesLiteralTriple_AndRoundTrips()
    {
        var value = "{\n  \"x\": 10,\n  \"y\": 20,\n  \"label\": \"hello\"\n}";

        AssertRoundTripsAsString("config", value);

        var composed = SidecarHelper.Compose(new Dictionary<string, object> { ["config"] = value });
        composed.Should().Contain("'''");
    }

    [Test]
    public void PythonDocstringWithTripleSingleQuotes_UsesBasicTriple_AndRoundTrips()
    {
        var value = "def f():\n    '''docstring with triple-quotes inside'''\n    return 0\n";

        AssertRoundTripsAsString("code", value);

        var composed = SidecarHelper.Compose(new Dictionary<string, object> { ["code"] = value });
        composed.Should().Contain("\"\"\"");
    }

    [Test]
    public void LargeJsonNodeConfig_UsesLiteralTriple_AndRoundTrips()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append('{');
        for (int i = 0; i < 200; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            builder.Append('\n');
            builder.Append($"  \"key{i}\": {{\n    \"value\": {i},\n    \"label\": \"item-{i}\"\n  }}");
        }
        builder.Append("\n}");
        var value = builder.ToString();

        AssertRoundTripsAsString("nodes", value);
    }

    [Test]
    public void EncodingTheSameValueTwice_ProducesByteIdenticalOutput()
    {
        var fields = new Dictionary<string, object>
        {
            ["title"] = "Sunset",
            ["summary"] = "Photo shot at golden hour.",
            ["_tags"] = new List<object> { "draft", "photo" },
        };

        var first = SidecarHelper.Compose(fields);
        var second = SidecarHelper.Compose(fields);

        second.Should().Be(first);
    }

    [Test]
    public void NoOpRoundTrip_DoesNotChangeBytes()
    {
        // Read back what we composed, then compose again — the bytes must match.
        // This is what the SidecarService idempotency contract relies on.
        var fields = new Dictionary<string, object>
        {
            ["title"] = "Sunset",
            ["_tags"] = new List<object> { "draft" },
        };

        var composed = SidecarHelper.Compose(fields);
        var parseResult = SidecarHelper.Parse(composed);
        parseResult.IsSuccess.Should().BeTrue();

        var recomposed = SidecarHelper.Compose(parseResult.Value);
        recomposed.Should().Be(composed);
    }

    [Test]
    public void ReservedFieldsAppearFirst_UserFieldsAlphabeticalBelow()
    {
        var fields = new Dictionary<string, object>
        {
            ["zeta"] = "z",
            ["alpha"] = "a",
            ["_tags"] = new List<object> { "x" },
            ["mu"] = "m",
        };

        var composed = SidecarHelper.Compose(fields);
        var lines = composed.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("_tags");
        lines[1].Should().StartWith("alpha");
        lines[2].Should().StartWith("mu");
        lines[3].Should().StartWith("zeta");
    }

    [Test]
    public void FieldOrdering_StableAcrossInsertionOrder()
    {
        var first = new Dictionary<string, object>
        {
            ["alpha"] = "a",
            ["_tags"] = new List<object> { "x" },
            ["beta"] = "b",
        };
        var second = new Dictionary<string, object>
        {
            ["beta"] = "b",
            ["alpha"] = "a",
            ["_tags"] = new List<object> { "x" },
        };

        SidecarHelper.Compose(first).Should().Be(SidecarHelper.Compose(second));
    }

    [Test]
    public void UnknownReservedField_IsPreserved_AndAppearsBetweenKnownReservedAndUserFields()
    {
        var fields = new Dictionary<string, object>
        {
            ["title"] = "doc",
            ["_my_thing"] = "value",
            ["_tags"] = new List<object> { "x" },
        };

        var composed = SidecarHelper.Compose(fields);
        var lines = composed.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("_tags");
        lines[1].Should().StartWith("_my_thing");
        lines[2].Should().StartWith("title");

        // Hand-edited unknown reserved field round-trips through Parse-Compose.
        var parseResult = SidecarHelper.Parse(composed);
        parseResult.IsSuccess.Should().BeTrue();
        var recomposed = SidecarHelper.Compose(parseResult.Value);
        recomposed.Should().Be(composed);
    }

    [Test]
    public void ListOfStrings_PicksQuoteStylePerElement_AndRoundTrips()
    {
        var value = new List<object>
        {
            "plain",
            @"C:\path\with\backslashes",
            "multi\nline\nvalue",
            "has '''triple''' single quotes",
        };

        var fields = new Dictionary<string, object> { ["mixed"] = value };
        var composed = SidecarHelper.Compose(fields);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        var parsed = SidecarHelper.ExtractStringList(parseResult.Value.Fields["mixed"]);
        parsed.Count.Should().Be(4);
        parsed[0].Should().Be("plain");
        parsed[1].Should().Be(@"C:\path\with\backslashes");
        parsed[2].Should().Be("multi\nline\nvalue");
        parsed[3].Should().Be("has '''triple''' single quotes");
    }

    [Test]
    public void EmptyString_EmittedAsEmptyBasicString_AndRoundTrips()
    {
        var fields = new Dictionary<string, object> { ["empty"] = string.Empty };
        var composed = SidecarHelper.Compose(fields);

        composed.Should().Contain("empty = \"\"");

        var parseResult = SidecarHelper.Parse(composed);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Fields["empty"].Should().Be(string.Empty);
    }

    [Test]
    public void ValueContainingDoubleQuotes_RoundTrips()
    {
        var value = "She said \"hello\" loudly";

        AssertRoundTripsAsString("greeting", value);
    }

    [Test]
    public void ValueContainingSingleQuotes_RoundTrips()
    {
        // Single quotes are fine inside a basic string literal; the issue is
        // only `'''` runs inside a literal triple-quoted form.
        var value = "don't stop";

        AssertRoundTripsAsString("phrase", value);
    }

    [Test]
    public void ValueEndingInSingleQuote_RoundTripsThroughBasicForm()
    {
        // A literal triple-quoted string cannot end with `'` (it would merge
        // into the closing delimiter). The encoder falls through to the basic
        // form here.
        var value = "trailing'";

        AssertRoundTripsAsString("ends", value);
    }

    [Test]
    public void MultiLineValueWithLeadingBackslash_RoundTrips()
    {
        // Verifies the literal-triple form preserves backslashes verbatim
        // without escape leakage.
        var value = "line one\n\\ literal backslash\nline three\n";

        AssertRoundTripsAsString("body", value);
    }

    [Test]
    public void TripleQuoteRunsOfFourPlusInBasicTripleForm_RoundTrip()
    {
        // Forces the basic-triple escape path and exercises run-breaking on a
        // long run of double quotes.
        var value = "before '''start'''\nrun of \"\"\"\"\"\" quotes\nend";

        AssertRoundTripsAsString("body", value);
    }

    private static void AssertRoundTripsAsString(string key, string value)
    {
        var fields = new Dictionary<string, object> { [key] = value };
        var composed = SidecarHelper.Compose(fields);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue(because: $"compose output should parse: '{composed}'");
        parseResult.Value.Fields[key].Should().Be(value);
    }
}
