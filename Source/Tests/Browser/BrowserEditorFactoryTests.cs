using Celbridge.Browser.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class BrowserEditorFactoryTests
{
    private BrowserEditorFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        _factory = new BrowserEditorFactory(serviceProvider, stringLocalizer);
    }

    [Test]
    public void SupportedExtensions_IncludesDotWebview()
    {
        _factory.SupportedExtensions.Should().Contain(".browser");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeDotWebviewCel()
    {
        // .cel is reserved for project metadata sidecars. The browser editor binds to the plain .browser extension,
        // and the document's data goes in the .browser file as TOML.
        _factory.SupportedExtensions.Should().NotContain(".browser.cel");
    }

    [Test]
    public void SupportedExtensions_DoesNotIncludeLegacyDotWebapp()
    {
        _factory.SupportedExtensions.Should().NotContain(".webapp");
    }
}
