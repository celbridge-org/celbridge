using Celbridge.WebView.Helpers;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewUrlHelperTests
{
    [Test]
    public void TryNormalize_AddsHttpsWhenNoSchemeWasTyped()
    {
        WebViewUrlHelper.TryNormalize("example.com", out var url).Should().BeTrue();

        url.Should().Be("https://example.com");
    }

    [Test]
    public void TryNormalize_AcceptsALocalServer()
    {
        WebViewUrlHelper.TryNormalize("http://localhost:5173", out var url).Should().BeTrue();

        url.Should().Be("http://localhost:5173");
    }

    [Test]
    public void TryNormalize_RejectsANonWebScheme()
    {
        WebViewUrlHelper.TryNormalize("file:///c:/notes.txt", out _).Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_IdenticalAddresses_IsTrue()
    {
        WebViewUrlHelper.IsSameUrl("https://example.com/docs", "https://example.com/docs").Should().BeTrue();
    }

    [Test]
    public void IsSameUrl_DifferingOnlyByATrailingSlash_IsTrue()
    {
        // Navigating rewrites an address to its absolute form, so a hand-typed bookmark differs from the
        // page it opens by a trailing slash alone.
        WebViewUrlHelper.IsSameUrl("https://example.com", "https://example.com/").Should().BeTrue();
    }

    [Test]
    public void IsSameUrl_DifferentPages_IsFalse()
    {
        WebViewUrlHelper.IsSameUrl("https://example.com/docs", "https://example.com/other").Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_TwoAddressesThatCannotBeNavigatedTo_IsFalse()
    {
        // Neither names a page, so neither can be the same page as the other. Reducing both to a blank
        // comparison key would report them as matching.
        WebViewUrlHelper.IsSameUrl("file:///c:/one.txt", "file:///c:/two.txt").Should().BeFalse();
    }

    [Test]
    public void IsSameUrl_AnEmptyAddress_IsFalse()
    {
        WebViewUrlHelper.IsSameUrl(string.Empty, "https://example.com").Should().BeFalse();
    }
}
