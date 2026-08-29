using Celbridge.Tests.Localization;
using Celbridge.UserInterface.Services;
using Celbridge.Workshop;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies the invariants every section in the Workshop catalog has to satisfy.
/// </summary>
[TestFixture]
public class WorkshopSectionCatalogTests
{
    [Test]
    public void All_EverySectionIsFullyDeclared()
    {
        foreach (var section in WorkshopSections.All)
        {
            section.NameKey.Should().NotBeEmpty();
            section.IconName.Should().NotBeEmpty();

            // A .webview document only accepts an external page URL, and these pages are all served over TLS.
            section.Url.Should().StartWith("https://");
        }
    }

    [Test]
    public void All_UrlsAreUnique()
    {
        // Two sections at the same address would draw two bookmarks that do the same thing.
        var urls = WorkshopSections.All.Select(section => section.Url);

        urls.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void All_EveryNameKeyHasALocalizedString()
    {
        // A key with no entry bakes the raw key into the document as the bookmark's label.
        var strings = TestLocalizerService.LoadStrings();

        foreach (var section in WorkshopSections.All)
        {
            strings.Should().ContainKey(section.NameKey);
        }
    }

    [Test]
    public void All_EveryIconResolvesInTheBundledFont()
    {
        var iconService = new IconService();

        foreach (var section in WorkshopSections.All)
        {
            iconService.TryGetGlyph(section.IconName, out _).Should().BeTrue(
                $"section '{section.NameKey}' names icon '{section.IconName}'");
        }
    }

    [Test]
    public void All_TheLandingPageIsAlsoABookmark()
    {
        // The landing page is the document's Home target as well as a bookmark, so the bookmark bar alone
        // is a complete way around the site.
        WorkshopSections.All.Should().Contain(WorkshopSections.Celbridge);
    }
}
