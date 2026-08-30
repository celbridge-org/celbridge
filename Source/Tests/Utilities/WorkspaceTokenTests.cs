using Celbridge.Documents;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Tests for WorkspaceAreaTokens and DocumentSectionTokens — the wire spellings of areas and sections used
/// by manifests, stored layout and the MCP tools.
/// </summary>
[TestFixture]
public class WorkspaceTokenTests
{
    [Test]
    public void EveryArea_RoundTripsThroughItsToken()
    {
        foreach (var area in Enum.GetValues<WorkspaceArea>())
        {
            var token = area.ToToken();

            WorkspaceAreaTokens.TryParse(token, out var parsed).Should().BeTrue();
            parsed.Should().Be(area);
        }
    }

    [Test]
    public void EverySection_RoundTripsThroughItsToken()
    {
        foreach (var section in Enum.GetValues<DocumentSection>())
        {
            var token = section.ToToken();

            DocumentSectionTokens.TryParse(token, out var parsed).Should().BeTrue();
            parsed.Should().Be(section);
        }
    }

    [Test]
    public void UnmappedArea_Throws()
    {
        // The tokens are a wire format. An area added without a token here would otherwise be written to
        // stored layout as a different area that already means something.
        var unmapped = (WorkspaceArea)999;

        var act = () => unmapped.ToToken();

        act.Should().Throw<NotSupportedException>();
    }

    [Test]
    public void UnmappedSection_Throws()
    {
        var unmapped = (DocumentSection)999;

        var act = () => unmapped.ToToken();

        act.Should().Throw<NotSupportedException>();
    }

    [TestCase("panel", Description = "not an area")]
    [TestCase("Utility", Description = "wrong case")]
    [TestCase("", Description = "empty")]
    [TestCase(null, Description = "absent")]
    public void UnrecognizedAreaToken_ParsesAsFalse(string? token)
    {
        WorkspaceAreaTokens.TryParse(token, out _).Should().BeFalse();
    }

    [TestCase("mainleft", Description = "missing separator")]
    [TestCase("MainLeft", Description = "the enum name rather than the token")]
    [TestCase("main", Description = "an area rather than a section")]
    public void UnrecognizedSectionToken_ParsesAsFalse(string? token)
    {
        DocumentSectionTokens.TryParse(token, out _).Should().BeFalse();
    }
}
