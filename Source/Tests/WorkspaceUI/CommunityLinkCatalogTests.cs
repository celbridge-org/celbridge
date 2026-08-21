using Celbridge.Community;
using Celbridge.Tests.Localization;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies the invariants a community link entry has to satisfy, so adding the next link to the catalog is a
/// complete change rather than a partly wired button.
/// </summary>
[TestFixture]
public class CommunityLinkCatalogTests
{
    [Test]
    public void All_EveryLinkIsFullyDeclared()
    {
        foreach (var link in CommunityLinks.All)
        {
            link.LinkId.Should().NotBeEmpty();
            link.DocumentName.Should().NotBeEmpty();
            link.TooltipKey.Should().NotBeEmpty();

            // A .webview document only accepts an external page URL, and these pages are all served over TLS.
            link.Url.Should().StartWith("https://");
        }
    }

    [Test]
    public void All_LinkIdsAreUnique()
    {
        var linkIds = CommunityLinks.All.Select(link => link.LinkId);

        linkIds.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void All_DocumentNamesAreUnique()
    {
        // Two links sharing a document name would share a document, so clicking one would show the other.
        var documentNames = CommunityLinks.All.Select(link => link.DocumentName);

        documentNames.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void All_EveryTooltipKeyHasALocalizedString()
    {
        // A key with no entry shows the raw key as the rail button tooltip.
        var strings = TestLocalizerService.LoadStrings();

        foreach (var link in CommunityLinks.All)
        {
            strings.Should().ContainKey(link.TooltipKey);
        }
    }

    [Test]
    public void LandmarkId_FollowsTheRailButtonConvention()
    {
        CommunityLinks.Forum.LandmarkId.Should().Be("forum-utility-button");
    }
}
