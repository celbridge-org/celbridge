using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class SidecarHelperTests
{
    [Test]
    public void Parse_RoundTripsFrontmatterPlusBody()
    {
        var text = "+++\ntags = [\"a\", \"b\"]\npriority = \"high\"\n+++\n# Body\n\nContent here.";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        var parsed = result.Value;
        parsed.Frontmatter.Should().ContainKey("priority");
        parsed.Frontmatter["priority"].Should().Be("high");
        parsed.Body.Should().StartWith("# Body");
    }

    [Test]
    public void Parse_AcceptsFrontmatterOnlyFile()
    {
        var text = "+++\ntags = [\"meeting\"]\n+++\n";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frontmatter.Should().ContainKey("tags");
        result.Value.Body.Should().Be(string.Empty);
    }

    [Test]
    public void Parse_AcceptsFrontmatterOnlyWithoutTrailingNewline()
    {
        var text = "+++\nkey = \"value\"\n+++";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frontmatter["key"].Should().Be("value");
        result.Value.Body.Should().Be(string.Empty);
    }

    [Test]
    public void Parse_RejectsContentWithoutOpeningFence()
    {
        var text = "just a body, no fence at all";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Parse_RejectsContentWithoutClosingFence()
    {
        var text = "+++\nkey = \"value\"\nno closing fence here";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Parse_TreatsBodyVerbatimIncludingInternalFences()
    {
        // Only the *first* closing +++ terminates the frontmatter. Any further
        // +++ lines belong to the body verbatim.
        var text = "+++\nkey = \"value\"\n+++\nbody line 1\n+++\nbody line 2";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Body.Should().Contain("body line 1");
        result.Value.Body.Should().Contain("+++");
        result.Value.Body.Should().Contain("body line 2");
    }

    [Test]
    public void Compose_OutputRoundTripsThroughParse()
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["title"] = "My Notes",
            ["tags"] = new List<object> { "meeting", "todo" },
        };
        var body = "# Meeting notes\n\nDecisions here.";

        var composed = SidecarHelper.Compose(frontmatter, body);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Frontmatter["title"].Should().Be("My Notes");
        parseResult.Value.Body.TrimEnd().Should().Be(body.TrimEnd());
    }

    [Test]
    public void Compose_HandlesEmptyBody()
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["editor_id"] = "celbridge.notes",
        };

        var composed = SidecarHelper.Compose(frontmatter, string.Empty);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Body.Should().Be(string.Empty);
    }

    [Test]
    public void Compose_HandlesEmptyFrontmatter()
    {
        var composed = SidecarHelper.Compose(new Dictionary<string, object>(), "hello body");
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Frontmatter.Should().BeEmpty();
        parseResult.Value.Body.Should().Contain("hello body");
    }
}
