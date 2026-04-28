using Celbridge.Settings;
using Celbridge.WebHost.Services;

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

    [TestCase("https://example.com")]
    [TestCase("HTTP://EXAMPLE.COM")]
    [TestCase("https://example.com/path?q=1")]
    [TestCase("http://localhost:8080")]
    public void IsExternalUrl_HttpOrHttps_ReturnsTrue(string url)
    {
        _webViewService.IsExternalUrl(url).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("example.com")]
    [TestCase("readme.txt")]
    [TestCase("script.py")]
    [TestCase("local://Sites/index.html")]
    [TestCase("LOCAL://Sites/index.html")]
    [TestCase("index.html")]
    [TestCase("../index.html")]
    [TestCase("subfolder/page.htm")]
    [TestCase("99_archive/report/index.html")]
    public void IsExternalUrl_NonHttpInput_ReturnsFalse(string url)
    {
        _webViewService.IsExternalUrl(url).Should().BeFalse();
    }
}
