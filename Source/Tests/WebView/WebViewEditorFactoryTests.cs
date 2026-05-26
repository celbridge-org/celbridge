using Celbridge.WebView.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewEditorFactoryTests
{
    private WebViewEditorFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        _factory = new WebViewEditorFactory(serviceProvider, stringLocalizer);
    }

    [Test]
    public void SupportedExtensions_IncludesDotWebviewCel()
    {
        _factory.SupportedExtensions.Should().Contain(".webview.cel");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeLegacyDotWebview()
    {
        _factory.SupportedExtensions.Should().NotContain(".webview");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeDotWebapp()
    {
        _factory.SupportedExtensions.Should().NotContain(".webapp");
    }
}
