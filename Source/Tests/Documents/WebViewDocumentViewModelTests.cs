using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
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

    private WebViewDocumentViewModel CreateViewModel()
    {
        return new WebViewDocumentViewModel(_commandService, _webViewService)
        {
            FilePath = _tempFilePath,
            FileResource = new ResourceKey("test.webview")
        };
    }
}
