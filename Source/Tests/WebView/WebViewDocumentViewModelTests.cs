using Celbridge.Commands;
using Celbridge.Resources;
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
    private ISidecarService _sidecarService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        var featureFlags = Substitute.For<IFeatureFlags>();

        _sidecarService = Substitute.For<ISidecarService>();
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Sidecars.Returns(_sidecarService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _webViewService = new WebViewService(featureFlags, _workspaceWrapper);
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpUrl()
    {
        StubSidecarFrontmatter(new Dictionary<string, object>
        {
            ["source_url"] = "http://example.com",
        });

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("http://example.com");
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpsUrl()
    {
        StubSidecarFrontmatter(new Dictionary<string, object>
        {
            ["source_url"] = "https://example.com/path?q=1",
        });

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("https://example.com/path?q=1");
    }

    [Test]
    public async Task LoadContent_FailsOnLocalAbsoluteUrl()
    {
        StubSidecarFrontmatter(new Dictionary<string, object>
        {
            ["source_url"] = "local://Sites/index.html",
        });

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnLocalPathUrl()
    {
        StubSidecarFrontmatter(new Dictionary<string, object>
        {
            ["source_url"] = "../index.html",
        });

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnBrokenSidecar()
    {
        // A malformed .webview.cel should surface as a parse failure, not silently
        // open with an empty source_url. SidecarReadOutcome.Broken is the channel
        // SidecarService uses to report this.
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Broken, null, "bad TOML"))));

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("parse");
    }

    [Test]
    public async Task LoadContent_TreatsMissingSidecarAsBlankUrl()
    {
        // No file on disk: open with no URL configured rather than failing. The
        // inspector lets the user type a URL in afterward.
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null))));

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().BeEmpty();
    }

    [Test]
    public async Task LoadContent_HtmlViewer_IgnoresFileContents_AndSucceeds()
    {
        // The HtmlViewer role serves the HTML file directly via the project virtual
        // host without consulting any .webview.cel; SidecarService is never called.
        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper)
        {
            FilePath = "ignored.html",
            FileResource = new ResourceKey("page.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        await _sidecarService.DidNotReceive().ReadAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public void NavigateUrl_HtmlViewer_BuildsProjectVirtualHostUrlFromResourceKey()
    {
        var viewModel = new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper)
        {
            FileResource = new ResourceKey("Pages/welcome.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        viewModel.NavigateUrl.Should().Be("https://project.celbridge/Pages/welcome.html");
    }

    [Test]
    public async Task NavigateUrl_ExternalUrl_ReturnsSourceUrl()
    {
        StubSidecarFrontmatter(new Dictionary<string, object>
        {
            ["source_url"] = "https://example.com/x",
        });

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.NavigateUrl.Should().Be("https://example.com/x");
    }

    private void StubSidecarFrontmatter(IReadOnlyDictionary<string, object> frontmatter)
    {
        var content = new SidecarContent(frontmatter, Array.Empty<SidecarBlock>());
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));
    }

    private WebViewDocumentViewModel CreateViewModel()
    {
        return new WebViewDocumentViewModel(_commandService, _webViewService, _workspaceWrapper)
        {
            FileResource = new ResourceKey("test.webview.cel"),
        };
    }
}
