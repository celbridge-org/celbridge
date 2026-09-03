using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.Tests.Helpers;
using Celbridge.WebView.ViewModels;
using Celbridge.Workspace;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewDocumentViewModelTests
{
    private ICommandService _commandService = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IServerService _serverService = null!;
    private IStringLocalizer _stringLocalizer = null!;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        _serverService = Substitute.For<IServerService>();
        _serverService.Port.Returns(5000);

        _stringLocalizer = Substitute.For<IStringLocalizer>();

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        // Default: file exists on disk so reads are attempted. Per-test stubs
        // override individual behaviours.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.FileSystem.Returns(_resourceFileSystem);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpUrl()
    {
        StubWebViewFile("source_url = \"http://example.com\"");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("http://example.com");
    }

    [Test]
    public async Task LoadContent_AcceptsExternalHttpsUrl()
    {
        StubWebViewFile("source_url = \"https://example.com/path?q=1\"");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("https://example.com/path?q=1");
    }

    [Test]
    public async Task LoadContent_FailsOnLocalAbsoluteUrl()
    {
        StubWebViewFile("source_url = \"local://Sites/index.html\"");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnLocalPathUrl()
    {
        StubWebViewFile("source_url = \"../index.html\"");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_FailsOnInvalidToml()
    {
        // A malformed .webview file should surface as a parse failure, not silently
        // open with an empty source_url.
        StubWebViewFile("source_url = not quoted");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("parse");
    }

    [Test]
    public async Task LoadContent_TreatsMissingFileAsBlankUrl()
    {
        // No file on disk: open with no URL configured rather than failing. The
        // settings surface lets the user configure a URL afterward.
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
    public async Task LoadContent_ShowUrlBarDefaultsToTrue()
    {
        StubWebViewFile("source_url = \"https://example.com\"");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.ShowUrlBar.Should().BeTrue();
        viewModel.IsUrlBarVisible.Should().BeTrue();
    }

    [Test]
    public async Task LoadContent_ReadsShowUrlBarFalse()
    {
        StubWebViewFile(
            """
            source_url = "https://example.com"
            show_url_bar = false
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.ShowUrlBar.Should().BeFalse();
        viewModel.IsUrlBarVisible.Should().BeFalse();
    }

    [Test]
    public async Task LoadContent_HtmlViewer_IgnoresFileContents_AndSucceeds()
    {
        // The HtmlViewer role serves the HTML file directly via the project virtual
        // host without consulting any .webview file. The resource file system is
        // never called for this role.
        var viewModel = new WebViewDocumentViewModel(new NullLogger<WebViewDocumentViewModel>(), _commandService, _workspaceWrapper, _serverService, _stringLocalizer)
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
    public void IsUrlBarVisible_HtmlViewer_IsFalse()
    {
        // The URL bar is external-URL chrome; the HTML viewer never shows it.
        var viewModel = new WebViewDocumentViewModel(new NullLogger<WebViewDocumentViewModel>(), _commandService, _workspaceWrapper, _serverService, _stringLocalizer)
        {
            FileResource = new ResourceKey("page.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        viewModel.IsUrlBarVisible.Should().BeFalse();
    }

    [Test]
    public void NavigateUrl_HtmlViewer_BuildsLoopbackProjectUrlFromResourceKey()
    {
        var viewModel = new WebViewDocumentViewModel(new NullLogger<WebViewDocumentViewModel>(), _commandService, _workspaceWrapper, _serverService, _stringLocalizer)
        {
            FileResource = new ResourceKey("Pages/welcome.html"),
            Role = WebViewDocumentRole.HtmlViewer,
        };

        // The HtmlViewer is served over the loopback file server's /project/ route on every head.
        viewModel.NavigateUrl.Should().Be("http://127.0.0.1:5000/project/Pages/welcome.html");
    }

    [Test]
    public async Task NavigateUrl_ExternalUrl_ReturnsSourceUrl()
    {
        StubWebViewFile("source_url = \"https://example.com/x\"");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.NavigateUrl.Should().Be("https://example.com/x");
    }

    [Test]
    public async Task ChangingSourceUrl_AfterLoad_MarksUnsavedChanges()
    {
        StubWebViewFile("source_url = \"https://example.com\"");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.HasUnsavedChanges.Should().BeFalse();

        viewModel.SourceUrl = "https://example.org";

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public void TryNormalizeUserUrl_PrefixesHttpsWhenNoScheme()
    {
        var viewModel = CreateViewModel();

        var isValid = viewModel.TryNormalizeUserUrl("example.com", out var url);

        isValid.Should().BeTrue();
        url.Should().Be("https://example.com");
    }

    [Test]
    public void TryNormalizeUserUrl_RejectsNonHttpScheme()
    {
        var viewModel = CreateViewModel();

        var isValid = viewModel.TryNormalizeUserUrl("file:///C:/secrets.txt", out _);

        isValid.Should().BeFalse();
    }

    [Test]
    public void IsHomeUrlInvalid_IsFalseForABlankHomeUrl()
    {
        // A blank Home URL is unconfigured, not wrong, so the settings panel shows no error for it.
        var viewModel = CreateViewModel();

        viewModel.IsHomeUrlInvalid.Should().BeFalse();
    }

    [Test]
    public void IsHomeUrlInvalid_IsTrueForANonNavigableHomeUrl()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceUrl = "file:///C:/secrets.txt";

        viewModel.IsHomeUrlInvalid.Should().BeTrue();
    }

    [Test]
    public void SetCurrentPageAsHome_AdoptsTheCurrentPage()
    {
        var viewModel = CreateViewModel();
        viewModel.SourceUrl = "https://example.com";
        viewModel.CurrentUrl = "https://example.com/page";

        viewModel.CanSetCurrentPageAsHome.Should().BeTrue();

        viewModel.SetCurrentPageAsHome();

        viewModel.SourceUrl.Should().Be("https://example.com/page");
        viewModel.CanSetCurrentPageAsHome.Should().BeFalse();
    }

    [Test]
    public async Task LoadContent_WithBookmarks_DoesNotMarkUnsavedChanges()
    {
        // The bookmarks arriving from disk are not edits, so a document that is only opened must not be
        // written straight back out.
        StubWebViewFile(
            """
            source_url = "https://example.com"

            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.Bookmarks.Should().ContainSingle();
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public async Task AddingABookmark_AfterLoad_MarksUnsavedChanges()
    {
        StubWebViewFile("source_url = \"https://example.com\"");

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.HasUnsavedChanges.Should().BeFalse();

        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com/docs")));

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public async Task EditingABookmark_AfterLoad_MarksUnsavedChanges()
    {
        StubWebViewFile(
            """
            source_url = "https://example.com"

            [[bookmarks]]
            url = "https://example.com/docs"
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.HasUnsavedChanges.Should().BeFalse();

        viewModel.Bookmarks[0].Name = "Docs";

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public async Task ChangingSourceUrl_AfterAFailedLoad_StillMarksUnsavedChanges()
    {
        // A document left open over a file that no longer parses is fixed from its settings, so the change
        // handler has to survive the failure that sent the user there.
        StubWebViewFile("source_url = ");

        var viewModel = CreateViewModel();
        var loadResult = await viewModel.LoadContent();
        loadResult.IsFailure.Should().BeTrue();

        viewModel.SourceUrl = "https://example.org";

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public void CanAddBookmarkFromCurrentPage_WithNoMatchingBookmark_IsTrue()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentUrl = "https://example.com/docs";
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com/other")));

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeTrue();
    }

    [Test]
    public void CanAddBookmarkFromCurrentPage_WithAMatchingBookmark_IsFalse()
    {
        // Opening a bookmark and then the settings should not offer to bookmark the page again.
        var viewModel = CreateViewModel();
        viewModel.CurrentUrl = "https://example.com/docs";
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com/docs")));

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeFalse();
    }

    [Test]
    public void CanAddBookmarkFromCurrentPage_MatchingBookmarkWithNoTrailingSlash_IsFalse()
    {
        // Navigating rewrites the address to its absolute form, so a hand-typed bookmark differs from the
        // page it opens by a trailing slash alone.
        var viewModel = CreateViewModel();
        viewModel.CurrentUrl = "https://example.com/";
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com")));

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeFalse();
    }

    [Test]
    public void CanAddBookmarkFromCurrentPage_AfterTheMatchingBookmarkIsRemoved_IsTrueAgain()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentUrl = "https://example.com/docs";
        var bookmark = viewModel.CreateBookmark(new WebViewBookmark("https://example.com/docs"));
        viewModel.Bookmarks.Add(bookmark);

        viewModel.Bookmarks.Remove(bookmark);

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeTrue();
    }

    [Test]
    public void ToolbarBookmarks_LeavesOutAnEntryThatCannotBeNavigatedTo()
    {
        // Adding a bookmark starts it blank, and the bar is on screen while it is filled in, so an entry
        // with no usable URL must not put a button there that does nothing.
        var viewModel = CreateViewModel();
        viewModel.Role = WebViewDocumentRole.ExternalUrl;
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com")));
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark(string.Empty)));

        viewModel.ToolbarBookmarks.Should().ContainSingle();
        viewModel.ToolbarBookmarks[0].Url.Should().Be("https://example.com");
    }

    [Test]
    public void IsBookmarksBarVisible_WithOnlyUnnavigableBookmarks_IsFalse()
    {
        var viewModel = CreateViewModel();
        viewModel.Role = WebViewDocumentRole.ExternalUrl;
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark(string.Empty)));

        viewModel.IsBookmarksBarVisible.Should().BeFalse();
    }

    [Test]
    public void IsBookmarksBarVisible_WhileTheSettingsAreOpen_StaysTrue()
    {
        // The bar doubles as a live preview of the bookmarks being edited, so it stays up with the
        // settings showing.
        var viewModel = CreateViewModel();
        viewModel.Role = WebViewDocumentRole.ExternalUrl;
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new WebViewBookmark("https://example.com")));

        viewModel.IsSettingsOpen = true;

        viewModel.IsBookmarksBarVisible.Should().BeTrue();
    }

    [Test]
    public void IsHomeEnabled_WhileTheSettingsAreOpen_StaysTrue()
    {
        // Home names a destination, so it stays live and returns the document area to the page.
        var viewModel = CreateViewModel();
        viewModel.Role = WebViewDocumentRole.ExternalUrl;
        viewModel.SourceUrl = "https://example.com";

        viewModel.IsSettingsOpen = true;

        viewModel.IsHomeEnabled.Should().BeTrue();
    }

    [Test]
    public void NavigationOnThePage_WhileTheSettingsAreOpen_StaysDisabled()
    {
        // Back, Forward and Reload act on a page that is not on screen, so they report as unavailable
        // rather than acting out of sight.
        var viewModel = CreateViewModel();
        viewModel.Role = WebViewDocumentRole.ExternalUrl;
        viewModel.CurrentUrl = "https://example.com";
        viewModel.CanGoBack = true;
        viewModel.CanGoForward = true;

        viewModel.IsSettingsOpen = true;

        viewModel.IsBackEnabled.Should().BeFalse();
        viewModel.IsForwardEnabled.Should().BeFalse();
        viewModel.IsReloadOrStopEnabled.Should().BeFalse();
    }

    [Test]
    public async Task LoadContent_CompletesASourceUrlWithNoScheme()
    {
        // A host typed into the Home URL field, or written by hand, opens the page it names rather than
        // failing the whole document.
        StubWebViewFile("source_url = \"example.com\"");
        var viewModel = CreateViewModel();

        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.SourceUrl.Should().Be("https://example.com");
    }

    [Test]
    public void SourceUrl_SetWithNoScheme_IsCompletedOnCommit()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceUrl = "example.com";

        viewModel.SourceUrl.Should().Be("https://example.com");
    }

    private void StubWebViewFile(string tomlContent)
    {
        _resourceFileSystem.ReadAllTextAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<string>.Ok(tomlContent)));
    }

    private WebViewDocumentViewModel CreateViewModel()
    {
        return new WebViewDocumentViewModel(new NullLogger<WebViewDocumentViewModel>(), _commandService, _workspaceWrapper, _serverService, _stringLocalizer)
        {
            FileResource = new ResourceKey("test.webview"),
        };
    }
}
