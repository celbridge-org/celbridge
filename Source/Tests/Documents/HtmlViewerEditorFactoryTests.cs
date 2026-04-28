using Celbridge.WebView.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

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
    public void Factory_PriorityIsSpecialized()
    {
        _factory.Priority.Should().Be(EditorPriority.Specialized);
    }

    [Test]
    public void Registry_HtmlExtensionResolvesToHtmlViewerByDefault_AndCodeEditorIsListedAsAlternate()
    {
        var registry = new DocumentEditorRegistry();

        var codeEditor = Substitute.For<IDocumentEditorFactory>();
        codeEditor.EditorId.Returns(new DocumentEditorId("celbridge.code-editor"));
        codeEditor.DisplayName.Returns("Code Editor");
        codeEditor.SupportedExtensions.Returns(new List<string> { ".html", ".htm" });
        codeEditor.Priority.Returns(EditorPriority.General);
        codeEditor.CanHandleResource(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(codeEditor);
        registry.RegisterFactory(_factory);

        var fileResource = new ResourceKey("page.html");
        var resolveResult = registry.GetFactory(fileResource, "/path/page.html");

        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(_factory);

        var alternates = registry.GetFactoriesForFileExtension(".html");
        alternates.Should().HaveCount(2);
        alternates[0].Should().Be(_factory);
        alternates[1].Should().Be(codeEditor);
    }
}
