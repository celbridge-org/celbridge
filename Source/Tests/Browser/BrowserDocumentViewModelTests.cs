using Celbridge.Browser.ViewModels;
using Celbridge.Browser.Views;
using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using SkiaSharp;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class BrowserDocumentViewModelTests
{
    private const string PageOne = "https://example.com/one";
    private const string PageTwo = "https://example.com/two";
    private const string PageThree = "https://example.com/three";

    private ICommandService _commandService = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private IWindowModeService _windowModeService = null!;
    private IMessengerService _messengerService = null!;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _windowModeService = Substitute.For<IWindowModeService>();
        _messengerService = new MessengerService();

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        // Default: the file exists on disk, so reads are attempted. Per-test stubs override individual behaviors.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.FileSystem.Returns(_resourceFileSystem);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private void StubBrowserFile(string tomlContent)
    {
        _resourceFileSystem.ReadAllTextAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<string>.Ok(tomlContent)));
    }

    private BrowserDocumentViewModel CreateViewModel()
    {
        return new BrowserDocumentViewModel(
            new NullLogger<BrowserDocumentViewModel>(),
            _commandService,
            _workspaceWrapper,
            _stringLocalizer,
            _windowModeService,
            _messengerService)
        {
            FileResource = new ResourceKey("test.browser"),
        };
    }

    // A document showing a page at each address, with the last one shown.
    private BrowserDocumentViewModel CreateViewModelWithTabs(params string[] urls)
    {
        var viewModel = CreateViewModel();
        viewModel.OpenStartTabs();

        foreach (var url in urls)
        {
            var bookmark = viewModel.CreateBookmark(new BrowserBookmark(url));
            viewModel.OpenBookmark(bookmark);
        }

        return viewModel;
    }

    private static string[] Addresses(BrowserDocumentViewModel viewModel)
    {
        return viewModel.Tabs.Select(tab => tab.CurrentUrl).ToArray();
    }

    [Test]
    public async Task LoadContent_ReadsTheBookmarks_WithoutMarkingUnsavedChanges()
    {
        // Bookmarks arriving from disk count as loaded, so opening a document leaves the file alone.
        StubBrowserFile("""
            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            open_on_start = true
            """);

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.Bookmarks.Should().ContainSingle();
        viewModel.Bookmarks[0].OpenOnStart.Should().BeTrue();
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public async Task LoadContent_FailsOnInvalidToml()
    {
        StubBrowserFile("not valid = = toml");

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("parse");
    }

    [Test]
    public async Task LoadContent_TreatsAMissingFileAsNoBookmarks()
    {
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None))));

        var viewModel = CreateViewModel();
        var result = await viewModel.LoadContent();

        result.IsSuccess.Should().BeTrue();
        viewModel.Bookmarks.Should().BeEmpty();
    }

    [Test]
    public async Task LoadContent_AgainLeavesTheOpenTabsInPlace()
    {
        StubBrowserFile("""
            [[bookmarks]]
            url = "https://example.com/one"
            open_on_start = true
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();
        viewModel.OpenStartTabs();
        viewModel.OpenBlankTab();

        await viewModel.LoadContent();

        viewModel.Tabs.Should().HaveCount(2);
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

        viewModel.TryNormalizeUserUrl("file:///C:/secrets.txt", out _).Should().BeFalse();
    }

    [Test]
    public void AddingABookmark_MarksUnsavedChanges()
    {
        var viewModel = CreateViewModel();

        viewModel.AddBookmark(new BrowserBookmark("https://example.com/docs"));

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [TestCase(nameof(BrowserBookmarkViewModel.Name))]
    [TestCase(nameof(BrowserBookmarkViewModel.OpenOnStart))]
    public async Task EditingABookmark_MarksUnsavedChanges(string property)
    {
        StubBrowserFile("""
            [[bookmarks]]
            url = "https://example.com/docs"
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();

        viewModel.HasUnsavedChanges.Should().BeFalse();

        if (property == nameof(BrowserBookmarkViewModel.Name))
        {
            viewModel.Bookmarks[0].Name = "Docs";
        }
        else
        {
            viewModel.Bookmarks[0].OpenOnStart = true;
        }

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public void TheFirstBookmarkAdded_HasOpenOnStartOn()
    {
        var viewModel = CreateViewModel();

        var first = viewModel.AddBookmark(new BrowserBookmark(PageOne));
        var second = viewModel.AddBookmark(new BrowserBookmark(PageTwo));

        first.OpenOnStart.Should().BeTrue();
        second.OpenOnStart.Should().BeFalse();
    }

    [Test]
    public void AddBookmarkFromCurrentPage_WithNoBookmarks_HasOpenOnStartOn()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        var bookmark = viewModel.AddBookmarkFromCurrentPage();

        bookmark!.OpenOnStart.Should().BeTrue();
    }

    [Test]
    public void ToggleBookmarkForCurrentPage_WithNoBookmark_AddsOneAndLeavesTheSettingsClosed()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        viewModel.ToggleBookmarkForCurrentPage();

        viewModel.IsCurrentPageBookmarked.Should().BeTrue();
        viewModel.Bookmarks.Should().ContainSingle();
        viewModel.IsSettingsOpen.Should().BeFalse();
    }

    [Test]
    public void ToggleBookmarkForCurrentPage_WithABookmark_RemovesOnlyThatOne()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.AddBookmark(new BrowserBookmark(PageOne));
        var otherBookmark = viewModel.AddBookmark(new BrowserBookmark(PageTwo));

        viewModel.ToggleBookmarkForCurrentPage();

        viewModel.IsCurrentPageBookmarked.Should().BeFalse();
        viewModel.Bookmarks.Should().ContainSingle().Which.Should().BeSameAs(otherBookmark);
        viewModel.IsSettingsOpen.Should().BeFalse();
    }

    [Test]
    public void TheBookmarkControls_FollowTheReadOnlyState()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (sender, args) => changedProperties.Add(args.PropertyName);

        viewModel.IsReadOnly = true;

        viewModel.IsBookmarkPageEnabled.Should().BeFalse();
        viewModel.CanAddBookmarkFromCurrentPage.Should().BeFalse();
        changedProperties.Should().Contain(new[]
        {
            nameof(BrowserDocumentViewModel.IsBookmarkPageEnabled),
            nameof(BrowserDocumentViewModel.CanAddBookmarkFromCurrentPage)
        });

        viewModel.IsReadOnly = false;

        viewModel.IsBookmarkPageEnabled.Should().BeTrue();
        viewModel.CanAddBookmarkFromCurrentPage.Should().BeTrue();
    }

    [Test]
    public void TheStar_InAReadOnlyFile_LeavesTheBookmarksAsTheyAre()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        viewModel.AddBookmark(new BrowserBookmark(PageTwo));
        viewModel.HasUnsavedChanges = false;
        viewModel.IsReadOnly = true;

        // PageTwo is shown and bookmarked, so a writable file would lose the bookmark.
        viewModel.ToggleBookmarkForCurrentPage();

        viewModel.Bookmarks.Should().ContainSingle();

        // PageOne has no bookmark, so a writable file would gain one.
        viewModel.ChooseTab(viewModel.Tabs[0]);
        viewModel.ToggleBookmarkForCurrentPage();

        viewModel.AddBookmarkFromCurrentPage().Should().BeNull();
        viewModel.Bookmarks.Should().ContainSingle();
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public void CanAddBookmarkFromCurrentPage_FollowsTheShownPage()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeTrue();

        var bookmark = viewModel.AddBookmark(new BrowserBookmark(PageOne));

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeFalse();
        viewModel.IsCurrentPageBookmarked.Should().BeTrue();
        viewModel.FindBookmarkForCurrentPage().Should().BeSameAs(bookmark);

        viewModel.Bookmarks.Remove(bookmark);

        viewModel.CanAddBookmarkFromCurrentPage.Should().BeTrue();
    }

    [Test]
    public void IsCurrentPageBookmarked_MatchesAcrossATrailingSlash()
    {
        // Navigating rewrites the address to the absolute form. A hand-typed bookmark can then differ from the page by a
        // trailing slash alone.
        var viewModel = CreateViewModelWithTabs("https://example.com/");
        viewModel.AddBookmark(new BrowserBookmark("https://example.com"));

        viewModel.IsCurrentPageBookmarked.Should().BeTrue();
    }

    [Test]
    public void FindBookmarkForCurrentPage_AfterTheBookmarkIsRepointed_ReturnsNull()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.AddBookmark(new BrowserBookmark(PageOne));

        bookmark.Url = PageTwo;

        viewModel.FindBookmarkForCurrentPage().Should().BeNull();
        viewModel.IsCurrentPageBookmarked.Should().BeFalse();
    }

    // A leading "www." is dropped when a dot would still remain. Other subdomains stay. A port stays where it differs
    // from the scheme's default. An internationalized domain is named in Unicode.
    [TestCase("https://www.example.com/docs", "example.com")]
    [TestCase("https://example.com/docs", "example.com")]
    [TestCase("https://WWW.Example.com/", "example.com")]
    [TestCase("https://docs.example.com/", "docs.example.com")]
    [TestCase("https://www.com/", "www.com")]
    [TestCase("http://localhost:5173/", "localhost:5173")]
    [TestCase("https://example.com:443/", "example.com")]
    [TestCase("https://www.example.com:8443/", "example.com:8443")]
    [TestCase("https://xn--bcher-kva.de/", "bücher.de")]
    [TestCase("https://www.xn--bcher-kva.de/", "bücher.de")]
    [TestCase("https://user:secret@www.example.com/", "example.com")]
    public void AddBookmarkFromCurrentPage_NamesTheBookmarkAfterItsSite(string pageUrl, string expectedName)
    {
        var viewModel = CreateViewModelWithTabs(pageUrl);

        var bookmark = viewModel.AddBookmarkFromCurrentPage();

        bookmark.Should().NotBeNull();
        bookmark!.Name.Should().Be(expectedName);
    }

    [Test]
    public void NavigableBookmarks_LeavesOutAnEntryThatCannotBeNavigatedTo()
    {
        // A new bookmark starts blank, so the list leaves out the entry while the user is still filling in the address.
        var viewModel = CreateViewModel();
        viewModel.AddBookmark(new BrowserBookmark("https://example.com"));
        viewModel.AddBookmark(new BrowserBookmark(string.Empty));

        viewModel.NavigableBookmarks.Should().ContainSingle()
            .Which.Url.Should().Be("https://example.com");
    }

    [Test]
    public void OpenStartTabs_OpensEachOpenOnStartBookmarkAndShowsTheFirst()
    {
        var viewModel = CreateViewModel();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne)));
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageTwo, OpenOnStart: true)));
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageThree, OpenOnStart: true)));

        viewModel.OpenStartTabs();

        Addresses(viewModel).Should().Equal(PageTwo, PageThree);
        viewModel.ShownTab.Should().BeSameAs(viewModel.Tabs[0]);
    }

    [Test]
    public void OpenStartTabs_WithNoOpenOnStartBookmark_LeavesZeroTabs()
    {
        var viewModel = CreateViewModel();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne)));

        viewModel.OpenStartTabs();

        viewModel.Tabs.Should().BeEmpty();
        viewModel.ShownTab.Should().BeNull();
        viewModel.IsEmptyStateVisible.Should().BeTrue();
        viewModel.IsPageOnScreen.Should().BeFalse();
    }

    [Test]
    public void RestoreEditorState_BringsBackTheTabsInOrderWithTheShownTab()
    {
        const string State = """
            {
              "settingsOpen": false,
              "settingsSectionKey": "Bookmarks",
              "tabs": [
                { "url": "https://example.com/one", "title": "One" },
                { "url": "https://example.com/two", "title": "Two" }
              ],
              "shownTabIndex": 1
            }
            """;

        var viewModel = CreateViewModel();

        viewModel.RestoreEditorState(State);

        viewModel.SettingsSectionKey.Should().Be("Bookmarks");
        Addresses(viewModel).Should().Equal(PageOne, PageTwo);
        viewModel.Tabs[0].Label.Should().Be("One");
        viewModel.ShownTab.Should().BeSameAs(viewModel.Tabs[1]);
        viewModel.Tabs[1].Opener.Should().BeNull();
    }

    [Test]
    public void RestoreEditorState_WithNoTabs_OpensTheOpenOnStartBookmarks()
    {
        var viewModel = CreateViewModel();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne, OpenOnStart: true)));

        viewModel.RestoreEditorState("""{ "settingsOpen": false, "settingsSectionKey": "", "tabs": [] }""");

        Addresses(viewModel).Should().Equal(PageOne);
    }

    [Test]
    public void RestoreEditorState_ThatFailsToParse_OpensTheOpenOnStartBookmarks()
    {
        var viewModel = CreateViewModel();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne, OpenOnStart: true)));

        viewModel.RestoreEditorState("this is not JSON");

        Addresses(viewModel).Should().Equal(PageOne);
        viewModel.TrySerializeEditorState().Should().NotBeNull();
    }

    [Test]
    public void TrySerializeEditorState_DuringAnOpen_ReportsNothing()
    {
        // A save before the open has settled the tabs would replace good state with empty tabs.
        var viewModel = CreateViewModel();

        viewModel.TrySerializeEditorState().Should().BeNull();
    }

    [Test]
    public void TheSavedState_RoundTripsTabsTheirOrderAndTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo, PageThree);
        viewModel.Tabs[1].Title = "Two";
        viewModel.ChooseTab(viewModel.Tabs[1]);

        var state = viewModel.TrySerializeEditorState();

        var restored = CreateViewModel();
        restored.RestoreEditorState(state!);

        Addresses(restored).Should().Equal(PageOne, PageTwo, PageThree);
        restored.ShownTab.Should().BeSameAs(restored.Tabs[1]);
        restored.ShownTab!.Label.Should().Be("Two");
    }

    [Test]
    public void MovingATab_ChangesTheOrderInTheSavedState()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo, PageThree);

        viewModel.MoveTab(viewModel.Tabs[2], 0);

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        Addresses(restored).Should().Equal(PageThree, PageOne, PageTwo);
    }

    [Test]
    public void TheSavedState_LeavesOutABlankTab_AndShowsTheTabNextToIt()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        var blank = viewModel.OpenBlankTab();
        viewModel.MoveTab(blank, 1);

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        Addresses(restored).Should().Equal(PageOne, PageTwo);
        restored.ShownTab!.CurrentUrl.Should().Be(PageTwo);
    }

    [Test]
    public void TheSavedState_WithABlankTabShownLast_ShowsTheTabToItsLeft()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        viewModel.OpenBlankTab();

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        restored.ShownTab!.CurrentUrl.Should().Be(PageTwo);
    }

    [Test]
    public void TheSavedState_WhereEveryTabIsBlank_HasNoTabs()
    {
        var viewModel = CreateViewModelWithTabs();
        viewModel.OpenBlankTab();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne, OpenOnStart: true)));

        var restored = CreateViewModel();
        restored.Bookmarks.Add(restored.CreateBookmark(new BrowserBookmark(PageOne, OpenOnStart: true)));
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        Addresses(restored).Should().Equal(PageOne);
    }

    [TestCase("blob:https://example.com/7f3a")]
    [TestCase("data:text/html,<p>Report</p>")]
    public void TheSavedState_LeavesOutAPageWithNoWebAddress(string url)
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        viewModel.Tabs[1].NotifyNavigationCommitted(url);

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        Addresses(restored).Should().Equal(PageOne);
        restored.ShownTab!.CurrentUrl.Should().Be(PageOne);
    }

    [Test]
    public void OpenBookmark_OpensANewTabAndClosesTheSettings()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.IsSettingsOpen = true;

        var tab = viewModel.OpenBookmark(viewModel.CreateBookmark(new BrowserBookmark(PageTwo)));

        Addresses(viewModel).Should().Equal(PageOne, PageTwo);
        viewModel.ShownTab.Should().BeSameAs(tab);
        viewModel.IsSettingsOpen.Should().BeFalse();
    }

    [Test]
    public void NavigateShownTab_WithZeroTabs_OpensATab()
    {
        var viewModel = CreateViewModelWithTabs();

        var tab = viewModel.NavigateShownTab(PageOne);

        viewModel.Tabs.Should().ContainSingle().Which.Should().BeSameAs(tab);
        viewModel.ShownTab.Should().BeSameAs(tab);
        Addresses(viewModel).Should().Equal(PageOne);
    }

    [Test]
    public void NavigateShownTab_SendsTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);

        viewModel.NavigateShownTab(PageThree).Should().BeSameAs(viewModel.Tabs[1]);
        Addresses(viewModel).Should().Equal(PageOne, PageThree);
    }

    [Test]
    public void NavigateShownTab_ToAnAddressThatIsNotAbsolute_OpensNothing()
    {
        var viewModel = CreateViewModelWithTabs();

        viewModel.NavigateShownTab("example.com").Should().BeNull();
        viewModel.Tabs.Should().BeEmpty();
    }

    // A tab sent to an address names the address in its absolute form, matching what the page reports on commit.
    [Test]
    public void ATabSentToAnAddress_NamesItInTheFormThePageCommits()
    {
        var viewModel = CreateViewModelWithTabs();

        var typed = viewModel.NavigateShownTab("https://example.com")!;
        var opened = viewModel.OpenBookmark(viewModel.CreateBookmark(new BrowserBookmark("example.com")))!;

        typed.CurrentUrl.Should().Be("https://example.com/");
        opened.CurrentUrl.Should().Be("https://example.com/");
    }

    [Test]
    public void ClosingTheShownTab_ShowsTheTabOnTheRight()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo, PageThree);
        viewModel.ChooseTab(viewModel.Tabs[1]);

        viewModel.CloseTab(viewModel.Tabs[1]);

        viewModel.ShownTab!.CurrentUrl.Should().Be(PageThree);
        viewModel.ShownTab.IsShown.Should().BeTrue();
    }

    [Test]
    public void ClosingTheShownLastTab_ShowsTheTabOnTheLeft()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);

        viewModel.CloseTab(viewModel.Tabs[1]);

        viewModel.ShownTab!.CurrentUrl.Should().Be(PageOne);
    }

    [Test]
    public void ClosingAHiddenTab_LeavesTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);

        viewModel.CloseTab(viewModel.Tabs[0]);

        viewModel.ShownTab!.CurrentUrl.Should().Be(PageTwo);
    }

    [Test]
    public void ClosingTheOnlyTab_LeavesZeroTabsAndTheHint()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        viewModel.CloseTab(viewModel.Tabs[0]);

        viewModel.Tabs.Should().BeEmpty();
        viewModel.ShownTab.Should().BeNull();
        viewModel.IsEmptyStateVisible.Should().BeTrue();
    }

    [Test]
    public void ABlankTab_ShowsTheHint()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        viewModel.OpenBlankTab();

        viewModel.IsEmptyStateVisible.Should().BeTrue();
        viewModel.IsPageOnScreen.Should().BeFalse();
    }

    [Test]
    public void EmptyPageBookmarks_OffersTheFirstNavigableBookmarksUpToTheLimit()
    {
        var viewModel = CreateViewModel();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(string.Empty)));
        var addresses = Enumerable.Range(1, BrowserDocumentViewModel.EmptyPageBookmarkLimit + 2)
            .Select(number => $"https://example.com/{number}")
            .ToList();
        foreach (var address in addresses)
        {
            viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(address)));
        }

        viewModel.EmptyPageBookmarks.Select(bookmark => bookmark.Url).Should()
            .Equal(addresses.Take(BrowserDocumentViewModel.EmptyPageBookmarkLimit));
    }

    [Test]
    public void ABlankTab_InADocumentWithABookmark_ListsTheBookmark()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageTwo)));
        viewModel.AreEmptyPageBookmarksVisible.Should().BeFalse();

        viewModel.OpenBlankTab();

        viewModel.AreEmptyPageBookmarksVisible.Should().BeTrue();
    }

    [Test]
    public void TheEmptyPageBookmarks_StayHidden_WithNoBookmarksAndWhileTheSettingsShow()
    {
        var viewModel = CreateViewModelWithTabs();
        viewModel.OpenBlankTab();
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(string.Empty)));
        viewModel.AreEmptyPageBookmarksVisible.Should().BeFalse();

        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne)));
        viewModel.IsSettingsOpen = true;

        viewModel.AreEmptyPageBookmarksVisible.Should().BeFalse();
    }

    [Test]
    public void APageThatFailedToLoad_ListsNoBookmarks()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageTwo)));

        viewModel.ShownTab!.NotifyNavigationCompleted(NavigationOutcome.Failed);

        viewModel.IsLoadFailedVisible.Should().BeTrue();
        viewModel.AreEmptyPageBookmarksVisible.Should().BeFalse();
    }

    [Test]
    public void AddingABookmark_WhileABlankTabShows_RaisesTheEmptyPageBookmarks()
    {
        var viewModel = CreateViewModelWithTabs();
        viewModel.OpenBlankTab();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.Bookmarks.Add(viewModel.CreateBookmark(new BrowserBookmark(PageOne)));

        raised.Should().Contain(nameof(BrowserDocumentViewModel.EmptyPageBookmarks))
            .And.Contain(nameof(BrowserDocumentViewModel.AreEmptyPageBookmarksVisible));
    }

    [Test]
    public void NavigateShownTab_ToABookmarkedPage_ShowsTheBookmarksIcon()
    {
        var favicon = new byte[] { 1, 2, 3 };
        var viewModel = CreateViewModelWithTabs(PageOne);
        var blankTab = viewModel.OpenBlankTab();
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo));
        bookmark.Favicon = favicon;
        viewModel.Bookmarks.Add(bookmark);

        var tab = viewModel.NavigateShownTab(PageTwo);

        tab.Should().BeSameAs(blankTab);
        Addresses(viewModel).Should().Equal(PageOne, PageTwo);
        blankTab.Favicon.Should().BeSameAs(favicon);
        viewModel.IsPageOnScreen.Should().BeTrue();
    }

    [Test]
    public void NavigateShownTab_ToABookmarkedPage_FromATabWithAPage_KeepsThePagesIcon()
    {
        var pageFavicon = new byte[] { 1, 2, 3 };
        var viewModel = CreateViewModelWithTabs(PageOne);
        var shownTab = viewModel.Tabs[0];
        shownTab.NotifyNavigationCommitted(PageOne);
        shownTab.Favicon = pageFavicon;
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo));
        bookmark.Favicon = new byte[] { 4, 5, 6 };
        viewModel.Bookmarks.Add(bookmark);

        var tab = viewModel.NavigateShownTab(PageTwo);

        tab.Should().BeSameAs(shownTab);
        shownTab.Favicon.Should().BeSameAs(pageFavicon);
    }

    [Test]
    public void ANewWindowRequest_FromTheUser_OpensATabAfterTheOpenerAndShowsIt()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        var opener = viewModel.Tabs[0];

        var tab = viewModel.OpenTabForNewWindow(PageThree, isUserInitiated: true, opener);

        viewModel.Tabs.IndexOf(tab!).Should().Be(1);
        viewModel.ShownTab.Should().BeSameAs(tab);
        tab!.Opener.Should().BeSameAs(opener);
        viewModel.IsPageOnScreen.Should().BeTrue();
    }

    [Test]
    public void ANewWindowRequest_WithNoUserInput_OpensNoTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        var tab = viewModel.OpenTabForNewWindow(PageThree, isUserInitiated: false, viewModel.Tabs[0]);

        tab.Should().BeNull();
        viewModel.Tabs.Should().ContainSingle();
    }

    [Test]
    public void ANewWindowRequestForAMailAddress_OpensNoTabAndGoesToTheSystem()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        var tab = viewModel.OpenTabForNewWindow("mailto:someone@example.com", isUserInitiated: true, viewModel.Tabs[0]);

        tab.Should().BeNull();
        viewModel.Tabs.Should().ContainSingle();
        _commandService.Received(1).Execute<IOpenSystemBrowserCommand>(
            Arg.Any<Action<IOpenSystemBrowserCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("tel:+15551234")]
    [TestCase("acme-app://open")]
    [TestCase("data:text/html,<p>Sign in</p>")]
    public void ANewWindowRequestForAnotherScheme_OpensNothing(string url)
    {
        // A page chooses this address, so the application passes it to no other program.
        var viewModel = CreateViewModelWithTabs(PageOne);

        var tab = viewModel.OpenTabForNewWindow(url, isUserInitiated: true, viewModel.Tabs[0]);

        tab.Should().BeNull();
        viewModel.Tabs.Should().ContainSingle();
        _commandService.DidNotReceiveWithAnyArgs().Execute<IOpenSystemBrowserCommand>();
    }

    [TestCase("")]
    [TestCase("about:blank")]
    [TestCase("https://example.com/signin")]
    [TestCase("http://localhost:5173/")]
    [TestCase("blob:https://example.com/3f1c")]
    public void ANewWindowRequestForAPage_OpensATab(string url)
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        var tab = viewModel.OpenTabForNewWindow(url, isUserInitiated: true, viewModel.Tabs[0]);

        tab.Should().NotBeNull();
    }

    [Test]
    public void APageClosingItself_ClosesItsTabAndShowsTheOpener()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        var opener = viewModel.Tabs[0];
        var tab = viewModel.OpenTabForNewWindow(PageThree, isUserInitiated: true, opener)!;

        viewModel.ClosePageTab(tab);

        viewModel.Tabs.Should().NotContain(tab);
        viewModel.ShownTab.Should().BeSameAs(opener);
    }

    [Test]
    public void APageClosingItselfInAHiddenTab_LeavesTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        var tab = viewModel.OpenTabForNewWindow(PageThree, isUserInitiated: true, viewModel.Tabs[0])!;
        var shown = viewModel.Tabs[2];
        viewModel.ChooseTab(shown);

        viewModel.ClosePageTab(tab);

        viewModel.ShownTab.Should().BeSameAs(shown);
    }

    [Test]
    public void ClosingAnOpener_DropsTheLinkFromItsNewWindow()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        var opener = viewModel.Tabs[0];
        var tab = viewModel.OpenTabForNewWindow(PageThree, isUserInitiated: true, opener)!;

        viewModel.CloseTab(opener);

        tab.Opener.Should().BeNull();
    }

    [Test]
    public void ANewTabsFirstNavigationBecomingADownload_ClosesTheTabAndShowsTheOpener()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var opener = viewModel.Tabs[0];
        var tab = viewModel.OpenTabForNewWindow("https://example.com/file.zip", isUserInitiated: true, opener)!;

        var closed = viewModel.HandleDownloadStarted(tab);

        closed.Should().BeTrue();
        viewModel.Tabs.Should().NotContain(tab);
        viewModel.ShownTab.Should().BeSameAs(opener);
    }

    [Test]
    public void ADownloadInATabWithAPage_KeepsTheTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var tab = viewModel.OpenTabForNewWindow(PageTwo, isUserInitiated: true, viewModel.Tabs[0])!;
        tab.NotifyNavigationCommitted(PageTwo);

        viewModel.HandleDownloadStarted(tab).Should().BeFalse();
        viewModel.Tabs.Should().Contain(tab);

        var userTab = viewModel.Tabs[0];
        viewModel.HandleDownloadStarted(userTab).Should().BeFalse();
    }

    [TestCase(LayoutMode.Default, true)]
    [TestCase(LayoutMode.Focus, true)]
    [TestCase(LayoutMode.Presentation, false)]
    public void TheToolbars_FollowTheLayoutMode(LayoutMode layoutMode, bool expected)
    {
        var viewModel = CreateViewModel();

        _messengerService.Send(new LayoutModeChangedMessage(layoutMode));

        viewModel.AreToolbarsVisible.Should().Be(expected);
    }

    [Test]
    public void ADocumentOpenedDuringPresentationMode_HidesTheToolbars()
    {
        _windowModeService.LayoutMode.Returns(LayoutMode.Presentation);

        var viewModel = CreateViewModel();

        viewModel.AreToolbarsVisible.Should().BeFalse();

        _messengerService.Send(new LayoutModeChangedMessage(LayoutMode.Default));

        viewModel.AreToolbarsVisible.Should().BeTrue();
    }

    [Test]
    public void ANewWindowInPresentationMode_StillOpensATab()
    {
        _windowModeService.LayoutMode.Returns(LayoutMode.Presentation);
        var viewModel = CreateViewModelWithTabs(PageOne);

        var tab = viewModel.OpenTabForNewWindow(PageTwo, isUserInitiated: true, viewModel.Tabs[0]);

        tab.Should().NotBeNull();
        viewModel.AreToolbarsVisible.Should().BeFalse();
    }

    [Test]
    public void NavigationOnThePage_WhileTheSettingsAreOpen_StaysDisabled()
    {
        // Back, Forward and Reload act on a page that is not on screen, so they report as unavailable.
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.ShownTab!.CanGoBack = true;
        viewModel.ShownTab.CanGoForward = true;

        viewModel.IsSettingsOpen = true;

        viewModel.IsBackEnabled.Should().BeFalse();
        viewModel.IsForwardEnabled.Should().BeFalse();
        viewModel.IsReloadOrStopEnabled.Should().BeFalse();
        viewModel.IsPageOnScreen.Should().BeFalse();
    }

    [Test]
    public void TheUrlBarProperties_FollowTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);
        viewModel.Tabs[0].CanGoBack = true;

        viewModel.AddressText.Should().Be(PageTwo);
        viewModel.IsBackEnabled.Should().BeFalse();

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ChooseTab(viewModel.Tabs[0]);

        viewModel.AddressText.Should().Be(PageOne);
        viewModel.IsBackEnabled.Should().BeTrue();
        changed.Should().Contain(nameof(BrowserDocumentViewModel.AddressText));
    }

    [Test]
    public void AHiddenTabsNavigation_LeavesTheUrlBarOnTheShownTab()
    {
        var viewModel = CreateViewModelWithTabs(PageOne, PageTwo);

        viewModel.Tabs[0].NotifyNavigationCommitted(PageThree);

        viewModel.AddressText.Should().Be(PageTwo);
    }

    [Test]
    public void ToBookmark_TrimsTheFieldsAndKeepsOpenOnStart()
    {
        var viewModel = CreateViewModel();
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(" https://example.com/one ", " One ", OpenOnStart: true));

        bookmark.ToBookmark().Should().Be(new BrowserBookmark(PageOne, "One", OpenOnStart: true));
    }

    [Test]
    public void ToBookmark_KeepsTheFavicon()
    {
        var favicon = TestFavicons.Png(16, 16);
        var viewModel = CreateViewModel();
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageOne, Favicon: favicon));

        bookmark.Favicon.Should().BeSameAs(favicon);
        bookmark.ToBookmark().Favicon.Should().BeSameAs(favicon);
    }

    // Loads a document with one Open on Start bookmark, and opens the bookmark's tab. The document has no unsaved edit
    // afterwards.
    private async Task<BrowserDocumentViewModel> LoadDocumentWithOneBookmarkAsync(byte[]? favicon = null)
    {
        var faviconLine = string.Empty;
        if (favicon is not null)
        {
            faviconLine = $"favicon = \"{Convert.ToBase64String(favicon)}\"";
        }

        StubBrowserFile($"""
            [[bookmarks]]
            url = "https://example.com/one"
            open_on_start = true
            {faviconLine}
            """);

        var viewModel = CreateViewModel();
        await viewModel.LoadContent();
        viewModel.OpenStartTabs();

        return viewModel;
    }

    // Reports an icon the way the view does. The helper begins a read of the tab's icon, and then reports the image
    // under that read's number.
    private static void ReportFavicon(BrowserDocumentViewModel viewModel, BrowserTabViewModel tab, byte[]? image)
    {
        var read = tab.BeginFaviconRead();

        viewModel.ReportTabFavicon(tab, read, image);
    }

    [Test]
    public async Task LoadContent_GivesABookmarkTheIconInItsFile()
    {
        var favicon = TestFavicons.Png(16, 16);

        var viewModel = await LoadDocumentWithOneBookmarkAsync(favicon);

        viewModel.Bookmarks[0].Favicon.Should().Equal(favicon);
        viewModel.Tabs[0].Favicon.Should().Equal(favicon);
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public async Task LoadContent_LeavesABookmarkWithNoIconInItsFileWithNone()
    {
        var viewModel = await LoadDocumentWithOneBookmarkAsync();

        viewModel.Bookmarks[0].Favicon.Should().BeNull();
        viewModel.Tabs[0].Favicon.Should().BeNull();
    }

    [Test]
    public void RestoringATab_UsesTheBookmarksIconOnlyWhenItsAddressMatchesABookmark()
    {
        var stored = TestFavicons.Png(16, 16);
        var viewModel = CreateViewModel();
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageOne, Favicon: stored));
        viewModel.Bookmarks.Add(bookmark);

        viewModel.RestoreEditorState("""
            {
              "settingsOpen": false,
              "settingsSectionKey": "",
              "tabs": [
                { "url": "https://example.com/one", "title": "" },
                { "url": "https://example.com/two", "title": "" }
              ]
            }
            """);

        viewModel.Tabs[0].Favicon.Should().BeSameAs(stored);
        viewModel.Tabs[1].Favicon.Should().BeNull();
    }

    [Test]
    public async Task ATabsFavicon_IsScaledAndStoredOnABookmarkWithNone_AsAnEdit()
    {
        var viewModel = await LoadDocumentWithOneBookmarkAsync();
        viewModel.HasUnsavedChanges.Should().BeFalse();

        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(64, 64));

        TestFavicons.SizeOf(viewModel.Tabs[0].Favicon!).Should().Be((32, 32));
        viewModel.Bookmarks[0].Favicon.Should().BeSameAs(viewModel.Tabs[0].Favicon);
        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public async Task ATabsFavicon_InAReadOnlyFile_ShowsOnTheTabAndIsNotStored()
    {
        var viewModel = await LoadDocumentWithOneBookmarkAsync();
        viewModel.IsReadOnly = true;

        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(64, 64));

        viewModel.Tabs[0].Favicon.Should().NotBeNull();
        viewModel.Bookmarks[0].Favicon.Should().BeNull();
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public async Task ABookmarkWithAnIcon_KeepsItWhenItsPageReportsAnother()
    {
        var stored = TestFavicons.Png(16, 16, SKColors.Red);
        var reported = TestFavicons.Png(16, 16, SKColors.Blue);
        var viewModel = await LoadDocumentWithOneBookmarkAsync(stored);

        ReportFavicon(viewModel, viewModel.Tabs[0], reported);

        TestFavicons.CenterColor(viewModel.Tabs[0].Favicon!).Should().Be(SKColors.Blue);
        viewModel.Bookmarks[0].Favicon.Should().Equal(stored);
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public void ATabsFavicon_IsStoredOnABookmarkAtAnotherFormOfTheAddress()
    {
        var viewModel = CreateViewModelWithTabs("https://example.com/");
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark("https://example.com"));
        viewModel.Bookmarks.Add(bookmark);

        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(16, 16));

        TestFavicons.SizeOf(bookmark.Favicon!).Should().Be((16, 16));
    }

    [Test]
    public void EveryBookmarkAtThePagesAddress_StoresTheIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var first = viewModel.CreateBookmark(new BrowserBookmark(PageOne));
        var second = viewModel.CreateBookmark(new BrowserBookmark(PageOne));
        viewModel.Bookmarks.Add(first);
        viewModel.Bookmarks.Add(second);

        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(16, 16));

        first.Favicon.Should().NotBeNull();
        second.Favicon.Should().BeSameAs(first.Favicon);
    }

    [Test]
    public void ATabsFavicon_IsNotStoredOnABookmarkAtAnotherAddress()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo));
        viewModel.Bookmarks.Add(bookmark);

        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(16, 16));

        bookmark.Favicon.Should().BeNull();
    }

    [Test]
    public void AnIconTheScalerCannotRead_LeavesTheTabsIconAndTheBookmarkAlone()
    {
        var shown = TestFavicons.Png(16, 16);
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageOne));
        viewModel.Bookmarks.Add(bookmark);
        viewModel.Tabs[0].Favicon = shown;

        ReportFavicon(viewModel, viewModel.Tabs[0], new byte[] { 1, 2, 3 });

        viewModel.Tabs[0].Favicon.Should().BeSameAs(shown);
        bookmark.Favicon.Should().BeNull();
    }

    [Test]
    public void AFaviconReadBeforeTheUserSendsTheTabElsewhere_IsDropped()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo));
        viewModel.Bookmarks.Add(bookmark);
        var tab = viewModel.Tabs[0];
        var read = tab.BeginFaviconRead();

        tab.NotifyUserNavigation(PageTwo);
        viewModel.ReportTabFavicon(tab, read, TestFavicons.Png(16, 16));

        tab.Favicon.Should().BeNull();
        bookmark.Favicon.Should().BeNull();
    }

    [Test]
    public void AFaviconReadBeforeANavigationStarts_IsDropped()
    {
        // A link's navigation changes the tab's address only when the navigation commits. The page that named the icon
        // is already leaving.
        var viewModel = CreateViewModelWithTabs(PageOne);
        var tab = viewModel.Tabs[0];
        var read = tab.BeginFaviconRead();

        tab.NotifyNavigationStarted(PageTwo);
        viewModel.ReportTabFavicon(tab, read, TestFavicons.Png(16, 16));

        tab.Favicon.Should().BeNull();
    }

    [Test]
    public void AFaviconRead_IsKeptWhenThePageMovesToAnotherAddressOfItsOwn()
    {
        // A fragment link or a script can change the page's address with no navigation. The page and its icon stay.
        var movedUrl = PageOne + "#details";
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(movedUrl));
        viewModel.Bookmarks.Add(bookmark);
        var tab = viewModel.Tabs[0];
        var read = tab.BeginFaviconRead();

        tab.NotifyNavigationCommitted(movedUrl);
        viewModel.ReportTabFavicon(tab, read, TestFavicons.Png(16, 16));

        tab.Favicon.Should().NotBeNull();
        bookmark.Favicon.Should().BeSameAs(tab.Favicon);
    }

    [Test]
    public void AFaviconRead_ThatALaterReadOvertakes_IsDropped()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var tab = viewModel.Tabs[0];
        var earlier = tab.BeginFaviconRead();
        var later = tab.BeginFaviconRead();

        viewModel.ReportTabFavicon(tab, later, TestFavicons.Png(16, 16, SKColors.Blue));
        viewModel.ReportTabFavicon(tab, earlier, TestFavicons.Png(16, 16, SKColors.Red));

        TestFavicons.CenterColor(tab.Favicon!).Should().Be(SKColors.Blue);
    }


    [Test]
    public void EditingABookmarksAddress_ClearsItsIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo, Favicon: TestFavicons.Png(16, 16)));
        viewModel.Bookmarks.Add(bookmark);

        bookmark.Url = PageThree;

        bookmark.Favicon.Should().BeNull();
    }

    [Test]
    public void EditingABookmarksAddress_ToAPageATabShows_CopiesTheTabsIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.Tabs[0].Favicon = TestFavicons.Png(32, 32);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageTwo, Favicon: TestFavicons.Png(16, 16)));
        viewModel.Bookmarks.Add(bookmark);

        bookmark.Url = PageOne;

        bookmark.Favicon.Should().BeSameAs(viewModel.Tabs[0].Favicon);
    }

    [Test]
    public void ARestoredTab_ShowsTheIconSavedWithIt()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(64, 64));

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        restored.Tabs[0].Favicon.Should().Equal(viewModel.Tabs[0].Favicon);
    }

    [Test]
    public void ARestoredTabsSavedIcon_TakesPrecedenceOverABookmarksIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        ReportFavicon(viewModel, viewModel.Tabs[0], TestFavicons.Png(16, 16, SKColors.Blue));

        var restored = CreateViewModel();
        var bookmarkIcon = TestFavicons.Png(16, 16, SKColors.Red);
        restored.Bookmarks.Add(restored.CreateBookmark(new BrowserBookmark(PageOne, Favicon: bookmarkIcon)));
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        TestFavicons.CenterColor(restored.Tabs[0].Favicon!).Should().Be(SKColors.Blue);
    }

    [Test]
    public void ARestoredTabWhoseSavedIconIsNotAPng_ShowsTheBookmarksIcon()
    {
        var bookmarkIcon = TestFavicons.Png(16, 16);
        var viewModel = CreateViewModel();
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageOne, Favicon: bookmarkIcon));
        viewModel.Bookmarks.Add(bookmark);
        var notAPng = Convert.ToBase64String(TestFavicons.Encode(16, 16, SKEncodedImageFormat.Jpeg));

        viewModel.RestoreEditorState($$"""
            {
              "settingsOpen": false,
              "settingsSectionKey": "",
              "tabs": [ { "url": "https://example.com/one", "title": "", "favicon": "{{notAPng}}" } ]
            }
            """);

        viewModel.Tabs[0].Favicon.Should().BeSameAs(bookmarkIcon);
    }

    [Test]
    public void ATabWithNoIcon_IsSavedWithNone()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);

        var restored = CreateViewModel();
        restored.RestoreEditorState(viewModel.TrySerializeEditorState()!);

        restored.Tabs[0].Favicon.Should().BeNull();
    }

    [Test]
    public void ATabGoingToAnotherSite_ForgetsItsIcon()
    {
        var viewModel = CreateViewModelWithTabs("https://one.example/");
        var tab = viewModel.Tabs[0];
        tab.NotifyNavigationCommitted("https://one.example/");
        ReportFavicon(viewModel, tab, TestFavicons.Png(16, 16));

        tab.NotifyNavigationCommitted("https://two.example/");

        tab.Favicon.Should().BeNull();
    }

    [Test]
    public void ATabMovingWithinItsSite_KeepsItsIcon()
    {
        var viewModel = CreateViewModelWithTabs("https://one.example/");
        var tab = viewModel.Tabs[0];
        tab.NotifyNavigationCommitted("https://one.example/");
        ReportFavicon(viewModel, tab, TestFavicons.Png(16, 16));

        tab.NotifyNavigationCommitted("https://one.example/other");

        tab.Favicon.Should().NotBeNull();
    }

    [Test]
    public void APageWithNoFavicon_LeavesTheBookmarksIconAlone()
    {
        var stored = TestFavicons.Png(16, 16);
        var viewModel = CreateViewModelWithTabs(PageOne);
        var bookmark = viewModel.CreateBookmark(new BrowserBookmark(PageOne, Favicon: stored));
        viewModel.Bookmarks.Add(bookmark);

        ReportFavicon(viewModel, viewModel.Tabs[0], null);

        bookmark.Favicon.Should().BeSameAs(stored);
    }

    [Test]
    public void ABookmarkAddedForAnOpenPage_StoresTheTabsIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.Tabs[0].Favicon = TestFavicons.Png(32, 32);

        var bookmark = viewModel.AddBookmarkFromCurrentPage();

        bookmark!.Favicon.Should().BeSameAs(viewModel.Tabs[0].Favicon);
    }

    [Test]
    public void ABookmarkAddedWithNoTabAtItsPage_HasNoIcon()
    {
        var viewModel = CreateViewModelWithTabs(PageOne);
        viewModel.Tabs[0].Favicon = TestFavicons.Png(16, 16);

        var bookmark = viewModel.AddBookmark(new BrowserBookmark(PageTwo));

        bookmark.Favicon.Should().BeNull();
    }
}
