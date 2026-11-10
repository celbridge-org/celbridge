using Celbridge.Browser.Helpers;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class BrowserUrlHelperTests
{
    [Test]
    public void TryNormalize_AddsHttpsWhenNoSchemeWasTyped()
    {
        BrowserUrlHelper.TryNormalize("example.com", out var url).Should().BeTrue();

        url.Should().Be("https://example.com");
    }

    [Test]
    public void TryNormalize_AcceptsALocalServer()
    {
        BrowserUrlHelper.TryNormalize("http://localhost:5173", out var url).Should().BeTrue();

        url.Should().Be("http://localhost:5173");
    }

    [TestCase("file:///c:/notes.txt")]
    [TestCase("local://Sites/index.html")]
    [TestCase("LOCAL://Sites/index.html")]
    public void TryNormalize_RejectsANonWebScheme(string url)
    {
        BrowserUrlHelper.TryNormalize(url, out _).Should().BeFalse();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("../index.html")]
    public void TryNormalize_RejectsAnAddressThatNamesNoHost(string url)
    {
        BrowserUrlHelper.TryNormalize(url, out _).Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_IdenticalAddresses_IsTrue()
    {
        BrowserUrlHelper.IsSameUrl("https://example.com/docs", "https://example.com/docs").Should().BeTrue();
    }

    [Test]
    public void IsSameUrl_DifferingOnlyByATrailingSlash_IsTrue()
    {
        // Navigating rewrites an address to its absolute form, so a hand-typed bookmark differs from the
        // page it opens by a trailing slash alone.
        BrowserUrlHelper.IsSameUrl("https://example.com", "https://example.com/").Should().BeTrue();
    }

    [Test]
    public void IsSameUrl_DifferentPages_IsFalse()
    {
        BrowserUrlHelper.IsSameUrl("https://example.com/docs", "https://example.com/other").Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_TwoAddressesThatCannotBeNavigatedTo_IsFalse()
    {
        // Neither names a page, so neither can be the same page as the other. Reducing both to a blank
        // comparison key would report them as matching.
        BrowserUrlHelper.IsSameUrl("file:///c:/one.txt", "file:///c:/two.txt").Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_AnEmptyAddress_IsFalse()
    {
        BrowserUrlHelper.IsSameUrl(string.Empty, "https://example.com").Should().BeFalse();
    }
}
