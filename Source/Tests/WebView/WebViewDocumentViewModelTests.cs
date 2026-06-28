using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.Workspace;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewDocumentViewModelTests
{
    private ICommandService _commandService = null!;
    private IWebViewService _webViewService = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IServerService _serverService = null!;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        var featureFlags = Substitute.For<IFeatureFlags>();

        _serverService = Substitute.For<IServerService>();
        _serverService.Port.Returns(5000);

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        // Default: file exists on disk so reads are attempted. Per-test stubs
        // override individual behaviours.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.FileSystem.Returns(_resourceFileSystem);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _webViewService = new WebViewService(featureFlags, _workspaceWrapper);
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpUrl()
    {
        StubWebViewFile("{\"sourceUrl\": \"http://example.com\"}");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("http://example.com");
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpsUrl()
    {
        StubWebViewFile("{\"sourceUrl\": \"https://example.com/path?q=1\"}");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("https://example.com/path?q=1");
    }

    [Test]
    public async Task LoadContent_FailsOnLocalAbsoluteUrl()
    {
        StubWebViewFile("{\"sourceUrl\": \"local://Sites/index.html\"}");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnLocalPathUrl()
    {
        StubWebViewFile("{\"sourceUrl\": \"../index.html\"}");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnInvalidJson()
    {
        // A malformed .webview file should surface as a parse failure, not silently
        // open with an empty sourceUrl.
        StubWebViewFile("{ not valid json ");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("parse");
    }

    [Test]
    public async Task LoadContent_TreatsMissingFileAsBlankUrl()
    {
        // No file on disk: open with no URL configured rather than failing. The
        // inspector lets the user type a URL in afterward.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None))));

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().BeEmpty();
    }

    [Test]
    public async Task LoadContent_TreatsEmptyFileAsBlankUrl()
    {
        // A blank file (e.g. just created via the Add File dialog) should load
        // cleanly with no URL configured.
        StubWebViewFile(string.Empty);

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().BeEmpty();
    }

    [Test]
    public async Task LoadContent_HtmlViewer_IgnoresFileContents_AndSucceeds()
    {
        // The HtmlViewer role serves the HTML file directly via the project virtual
        // host without consulting any .webview file; the resource file system is
        // never called for this role.
        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper, _serverService)
        {
            FilePath = "ignored.html",
            FileResource = new ResourceKey("page.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().ReadAllTextAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public void NavigateUrl_HtmlViewer_BuildsProjectVirtualHostUrlFromResourceKey()
    {
        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper, _serverService)
        {
            FileResource = new ResourceKey("Pages/welcome.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        // Windows serves the HtmlViewer via the project virtual host; other heads serve it over the
        // loopback file server (the virtual-host mapping is a no-op off Windows).
        var expectedUrl = OperatingSystem.IsWindows()
            ? "https://project.celbridge/Pages/welcome.html"
            : "http://127.0.0.1:5000/project/Pages/welcome.html";

        viewModel.NavigateUrl.Should().Be(expectedUrl);
    }

    [Test]
    public async Task NavigateUrl_ExternalUrl_ReturnsSourceUrl()
    {
        StubWebViewFile("{\"sourceUrl\": \"https://example.com/x\"}");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.NavigateUrl.Should().Be("https://example.com/x");
    }

    private void StubWebViewFile(string jsonContent)
    {
        _resourceFileSystem.ReadAllTextAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<string>.Ok(jsonContent)));
    }

    private WebViewDocumentViewModel CreateViewModel()
    {
        return new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper, _serverService)
        {
            FileResource = new ResourceKey("test.webview"),
        };
    }
}
