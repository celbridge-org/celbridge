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
    public void SupportedExtensions_IncludesDotWebview()
    {
        _factory.SupportedExtensions.Should().Contain(".webview");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeDotWebviewCel()
    {
        // .cel is reserved for project metadata sidecars; the .webview
        // editor binds to the plain .webview extension and stores its data
        // directly in the .webview file as JSON.
        _factory.SupportedExtensions.Should().NotContain(".webview.cel");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeLegacyDotWebapp()
    {
        _factory.SupportedExtensions.Should().NotContain(".webapp");
    }
}
