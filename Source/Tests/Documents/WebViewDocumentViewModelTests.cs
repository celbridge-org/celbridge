using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;

namespace Celbridge.Tests.Documents;

[TestFixture]
public class WebViewDocumentViewModelTests
{
    private string _tempFolder = null!;
    private string _tempFilePath = null!;
    private ICommandService _commandService = null!;
    private IWebViewService _webViewService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WebViewDocumentViewModelTests));
        Directory.CreateDirectory(_tempFolder);

        _tempFilePath = Path.Combine(_tempFolder, "test.webview");

        _commandService = Substitute.For<ICommandService>();
        var featureFlags = Substitute.For<IFeatureFlags>();
        _webViewService = new WebViewService(featureFlags);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpUrl()
    {
        await File.WriteAllTextAsync(_tempFilePath, """{"sourceUrl": "http://example.com"}""");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("http://example.com");
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpsUrl()
    {
        await File.WriteAllTextAsync(_tempFilePath, """{"sourceUrl": "https://example.com/path?q=1"}""");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("https://example.com/path?q=1");
    }

    [Test]
    public async Task LoadContent_FailsOnLocalAbsoluteUrl()
    {
        await File.WriteAllTextAsync(_tempFilePath, """{"sourceUrl": "local://Sites/index.html"}""");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnLocalPathUrl()
    {
        await File.WriteAllTextAsync(_tempFilePath, """{"sourceUrl": "../index.html"}""");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_HtmlViewer_IgnoresFileContents_AndSucceeds()
    {
        var htmlPath = Path.Combine(_tempFolder, "page.html");
        await File.WriteAllTextAsync(htmlPath, "<html><body>not JSON</body></html>");

        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService)
        {
            FilePath = htmlPath,
            FileResource = new ResourceKey("page.html"),
            Role = WebViewDocumentRole.HtmlViewer
        };

        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void NavigateUrl_HtmlViewer_BuildsProjectVirtualHostUrlFromResourceKey()
    {
        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService)
        {
            FilePath = _tempFilePath,
            FileResource = new ResourceKey("Pages/welcome.html"),
            Role = WebViewDocumentRole.HtmlViewer
        };

        viewModel.NavigateUrl.Should().Be("https://project.celbridge/Pages/welcome.html");
    }

    [Test]
    public async Task NavigateUrl_ExternalUrl_ReturnsSourceUrl()
    {
        await File.WriteAllTextAsync(_tempFilePath, """{"sourceUrl": "https://example.com/x"}""");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.NavigateUrl.Should().Be("https://example.com/x");
    }

    private WebViewDocumentViewModel CreateViewModel()
    {
        return new WebViewDocumentViewModel(_commandService, _webViewService)
        {
            FilePath = _tempFilePath,
            FileResource = new ResourceKey("test.webview")
        };
    }
}
