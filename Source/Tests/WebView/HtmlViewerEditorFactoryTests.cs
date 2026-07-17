using Celbridge.WebView.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class HtmlViewerEditorFactoryTests
{
    private HtmlViewerEditorFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        _factory = new HtmlViewerEditorFactory(serviceProvider, stringLocalizer);
    }

    [Test]
    public void Factory_RegistersHtmlAndHtm()
    {
        _factory.SupportedExtensions.Should().Contain(".html");
        _factory.SupportedExtensions.Should().Contain(".htm");
    }

    [Test]
    public void Factory_DoesNotRegisterWebView()
    {
        _factory.SupportedExtensions.Should().NotContain(".webview");
    }

    [Test]
    public void Registry_HtmlExtensionResolvesToHtmlViewerByDefault_AndCodeEditorIsListedAsAlternate()
    {
        // The HTML viewer sits ahead of the general code editor in the built-in host order, so it
        // is the default for .html even though the code editor registers first.
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var codeEditor = Substitute.For<IDocumentEditorFactory>();
        codeEditor.EditorId.Returns(DocumentConstants.CodeEditorId);
        codeEditor.DisplayName.Returns("Code Editor");
        codeEditor.SupportedExtensions.Returns(new List<string> { ".html", ".htm" });
        codeEditor.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);

        registry.RegisterFactory(codeEditor);
        registry.RegisterFactory(_factory);

        var fileResource = new ResourceKey("page.html");
        var resolveResult = registry.GetFactory(fileResource);

        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(_factory);

        var alternates = registry.GetFactoriesForExtension(".html");
        alternates.Should().HaveCount(2);
        alternates[0].Should().Be(_factory);
        alternates[1].Should().Be(codeEditor);
    }
}
