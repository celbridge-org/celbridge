using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class SidecarHelperTests
{
    [Test]
    public void Parse_RoundTripsFieldsOnlyFile()
    {
        var text = "_tags = [\"a\", \"b\"]\npriority = \"high\"\n";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        var parsed = result.Value;
        parsed.Fields.Should().ContainKey("priority");
        parsed.Fields["priority"].Should().Be("high");
    }

    [Test]
    public void Parse_AcceptsEmptyFile()
    {
        var result = SidecarHelper.Parse(string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Fields.Should().BeEmpty();
    }

    [Test]
    public void Parse_RejectsMalformedFieldsToml()
    {
        var text = "not valid toml at all = !!!";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Parse_TreatsLegacyFenceLineAsTomlContent()
    {
        // A sidecar predating the format change carries '+++ "..."' fence lines.
        // The TOML-only parser does not give them any special meaning; the file
        // is rejected as a TOML parse failure so it surfaces as Broken rather
        // than being silently migrated.
        var text = "tags = [\"meeting\"]\n+++ \"celbridge.notes.note-document.content\"\n# Meeting Notes\n";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Compose_RoundTripsFields()
    {
        var fields = new Dictionary<string, object>
        {
            ["title"] = "My Notes",
            ["_tags"] = new List<object> { "meeting", "todo" },
        };

        var composed = SidecarHelper.Compose(fields);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Fields["title"].Should().Be("My Notes");
    }

    [Test]
    public void Compose_EmitsLfLineEndings()
    {
        var fields = new Dictionary<string, object>
        {
            ["editor"] = "celbridge.test",
            ["_tags"] = new List<object> { "alpha", "beta" },
        };

        var composed = SidecarHelper.Compose(fields);

        composed.Should().NotContain("\r\n");
        composed.Should().NotContain("\r");
        composed.Should().Contain("\n");
    }

    [Test]
    public void Compose_EmptyFieldsReturnsEmpty()
    {
        var composed = SidecarHelper.Compose(new Dictionary<string, object>());

        composed.Should().Be(string.Empty);
    }
}
