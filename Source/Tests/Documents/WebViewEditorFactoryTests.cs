using Celbridge.WebView.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

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
    public void SupportedExtensions_IncludesDotWebview()
    {
        _factory.SupportedExtensions.Should().Contain(".webview");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeDotWebapp()
    {
        _factory.SupportedExtensions.Should().NotContain(".webapp");
    }
}
