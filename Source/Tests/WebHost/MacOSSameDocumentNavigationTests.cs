using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// The macOS head answers a navigation within the committed page itself, because Uno cancels every one of
/// them. These tests pin which destinations count as within the page, since every other navigation is
/// still Uno's to decide.
/// </summary>
[TestFixture]
public class MacOSSameDocumentNavigationTests
{
    private const string CommittedUrl = "http://127.0.0.1:8791/project/page.html";

    [Test]
    public void ALinkToAnAnchorInTheCommittedPage_IsWithinTheDocument()
    {
        MacOSWebViewInterop.IsSameDocument($"{CommittedUrl}#bottom", CommittedUrl)
            .Should().BeTrue();
    }

    [Test]
    public void AnAnchorReachedFromAnotherAnchor_IsWithinTheDocument()
    {
        // The committed URL keeps no fragment of its own, but a page already moved to one can move again.
        MacOSWebViewInterop.IsSameDocument($"{CommittedUrl}#bottom", $"{CommittedUrl}#top")
            .Should().BeTrue();
    }

    [Test]
    public void TheCommittedPageWithNoFragment_IsNotWithinTheDocument()
    {
        // A reload of the page loads content, so it belongs to the gate like any other navigation.
        MacOSWebViewInterop.IsSameDocument(CommittedUrl, CommittedUrl)
            .Should().BeFalse();
    }

    [Test]
    public void AnAnchorOnAnotherPage_IsNotWithinTheDocument()
    {
        MacOSWebViewInterop.IsSameDocument("http://127.0.0.1:8791/project/other.html#bottom", CommittedUrl)
            .Should().BeFalse();
    }

    [Test]
    public void TheSamePathWithAnotherQuery_IsNotWithinTheDocument()
    {
        MacOSWebViewInterop.IsSameDocument($"{CommittedUrl}?page=2#bottom", CommittedUrl)
            .Should().BeFalse();
    }

    [Test]
    public void AnAnchorOnAnotherSite_IsNotWithinTheDocument()
    {
        MacOSWebViewInterop.IsSameDocument("http://example.test/project/page.html#bottom", CommittedUrl)
            .Should().BeFalse();
    }

    [TestCase(null)]
    [TestCase("")]
    public void ANavigationWithNothingCommitted_IsNotWithinTheDocument(string? committedUrl)
    {
        // Nothing to compare against, so the navigation is decided the usual way.
        MacOSWebViewInterop.IsSameDocument($"{CommittedUrl}#bottom", committedUrl)
            .Should().BeFalse();
    }
}
