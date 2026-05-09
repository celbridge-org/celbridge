using Celbridge.Host;

namespace Celbridge.Tests.Host;

[TestFixture]
public class ToolAllowlistTests
{
    [Test]
    public void Matches_LiteralAlias_ReturnsTrueOnExactMatch()
    {
        ToolAllowlist.Matches("app.get_state", "app.get_state").Should().BeTrue();
        ToolAllowlist.Matches("app.get_state", "app.version").Should().BeFalse();
    }

    [Test]
    public void Matches_NamespaceWildcard_ReturnsTrueForMatchingPrefix()
    {
        ToolAllowlist.Matches("app.get_state", "app.*").Should().BeTrue();
        ToolAllowlist.Matches("document.open", "app.*").Should().BeFalse();
    }

    [Test]
    public void Matches_GlobalWildcard_MatchesEverything()
    {
        ToolAllowlist.Matches("anything.at.all", "*").Should().BeTrue();
        ToolAllowlist.Matches("one", "*").Should().BeTrue();
    }

    [Test]
    public void Matches_BarePrefix_DoesNotMatchAsWildcard()
    {
        ToolAllowlist.Matches("app.get_state", "app").Should().BeFalse();
    }

    [Test]
    public void Matches_EmptyInputs_ReturnsFalse()
    {
        ToolAllowlist.Matches("", "app.*").Should().BeFalse();
        ToolAllowlist.Matches("app.get_state", "").Should().BeFalse();
    }

    [Test]
    public void IsAllowed_EmptyAllowlist_ReturnsFalse()
    {
        ToolAllowlist.IsAllowed("app.get_state", Array.Empty<string>()).Should().BeFalse();
    }

    [Test]
    public void IsAllowed_AnyMatchingPattern_ReturnsTrue()
    {
        var patterns = new[] { "document.*", "app.get_state" };
        ToolAllowlist.IsAllowed("document.open", patterns).Should().BeTrue();
        ToolAllowlist.IsAllowed("app.get_state", patterns).Should().BeTrue();
        ToolAllowlist.IsAllowed("file.read", patterns).Should().BeFalse();
    }
}
