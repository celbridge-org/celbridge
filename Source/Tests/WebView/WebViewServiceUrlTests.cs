using Celbridge.Settings;
using Celbridge.WebView;
using Celbridge.WebView.Services;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewServiceUrlTests
{
    private WebViewService _webViewService = null!;

    [SetUp]
    public void SetUp()
    {
        var featureFlags = Substitute.For<IFeatureFlags>();
        _webViewService = new WebViewService(featureFlags);
    }

    [TestCase("https://example.com", UrlType.WebUrl)]
    [TestCase("HTTP://EXAMPLE.COM", UrlType.WebUrl)]
    [TestCase("https://example.com/path?q=1", UrlType.WebUrl)]
    [TestCase("http://localhost:8080", UrlType.WebUrl)]
    public void ClassifyUrl_WebUrl_ReturnsWebUrl(string url, UrlType expectedKind)
    {
        _webViewService.ClassifyUrl(url).Should().Be(expectedKind);
    }

    [TestCase("local://Sites/index.html", UrlType.LocalAbsolute)]
    [TestCase("local://99_archive/report/index.html", UrlType.LocalAbsolute)]
    [TestCase("LOCAL://Sites/index.html", UrlType.LocalAbsolute)]
    public void ClassifyUrl_LocalAbsolute_ReturnsLocalAbsolute(string url, UrlType expectedKind)
    {
        _webViewService.ClassifyUrl(url).Should().Be(expectedKind);
    }

    [TestCase("local://../index.html", UrlType.Invalid)]
    [TestCase("local://../../index.html", UrlType.Invalid)]
    [TestCase("local://", UrlType.Invalid)]
    [TestCase("local:///index.html", UrlType.Invalid)]
    [TestCase("local://path/../index.html", UrlType.Invalid)]
    public void ClassifyUrl_LocalAbsoluteWithRelativePath_ReturnsInvalid(string url, UrlType expectedKind)
    {
        _webViewService.ClassifyUrl(url).Should().Be(expectedKind);
    }

    [TestCase("index.html", UrlType.LocalPath)]
    [TestCase("../index.html", UrlType.LocalPath)]
    [TestCase("../../shared/index.html", UrlType.LocalPath)]
    [TestCase("subfolder/page.htm", UrlType.LocalPath)]
    [TestCase("99_archive/report/index.html", UrlType.LocalPath)]
    public void ClassifyUrl_LocalPath_ReturnsLocalPath(string url, UrlType expectedKind)
    {
        _webViewService.ClassifyUrl(url).Should().Be(expectedKind);
    }

    [TestCase("", UrlType.Invalid)]
    [TestCase("   ", UrlType.Invalid)]
    [TestCase("example.com", UrlType.Invalid)]
    [TestCase("readme.txt", UrlType.Invalid)]
    [TestCase("script.py", UrlType.Invalid)]
    public void ClassifyUrl_Invalid_ReturnsInvalid(string url, UrlType expectedKind)
    {
        _webViewService.ClassifyUrl(url).Should().Be(expectedKind);
    }

    [TestCase("https://example.com", false)]
    [TestCase("local://Sites/index.html", true)]
    [TestCase("../index.html", true)]
    [TestCase("index.html", true)]
    [TestCase("example.com", false)]
    [TestCase("", false)]
    public void NeedsFileServer_ReturnsExpected(string url, bool expected)
    {
        _webViewService.NeedsFileServer(url).Should().Be(expected);
    }

    [Test]
    public void StripLocalScheme_RemovesPrefix()
    {
        _webViewService.StripLocalScheme("local://Sites/index.html")
            .Should().Be("Sites/index.html");
    }

    [Test]
    public void StripLocalScheme_CaseInsensitive()
    {
        _webViewService.StripLocalScheme("LOCAL://Sites/index.html")
            .Should().Be("Sites/index.html");
    }

    [Test]
    public void StripLocalScheme_NoPrefix_ReturnsUnchanged()
    {
        _webViewService.StripLocalScheme("Sites/index.html")
            .Should().Be("Sites/index.html");
    }
}
