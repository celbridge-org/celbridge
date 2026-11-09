using Celbridge.Browser.ViewModels;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class BrowserTabViewModelTests
{
    private const string BlankLabel = "New Tab";

    private static BrowserTabViewModel CreateTabShowingPage(string url)
    {
        var tab = new BrowserTabViewModel(BlankLabel);
        tab.NotifyNavigationCommitted(url);

        return tab;
    }

    // Each address the tab gives the address bar from now on, in order.
    private static List<string> RecordAddresses(BrowserTabViewModel tab)
    {
        var addresses = new List<string>();

        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserTabViewModel.CurrentUrl))
            {
                addresses.Add(tab.CurrentUrl);
            }
        };

        return addresses;
    }

    [Test]
    public void Label_OfABlankTab_IsTheBlankLabel()
    {
        new BrowserTabViewModel(BlankLabel).Label.Should().Be(BlankLabel);
    }

    [Test]
    public void Label_IsTheAddressUntilThePageHasATitle()
    {
        var tab = BrowserTabViewModel.Create(BlankLabel, "https://example.com/docs");

        tab.Label.Should().Be("https://example.com/docs");

        tab.Title = "Docs";

        tab.Label.Should().Be("Docs");
    }

    [Test]
    public void Create_WithASavedTitle_NamesTheTabBeforeThePageLoads()
    {
        var tab = BrowserTabViewModel.Create(BlankLabel, "https://example.com/docs", "Docs");

        tab.Label.Should().Be("Docs");
        tab.CurrentUrl.Should().Be("https://example.com/docs");
    }

    [Test]
    public void AnAddressTheUserChose_ClearsTheOldTitle()
    {
        var tab = BrowserTabViewModel.Create(BlankLabel, "https://example.com/docs", "Docs");

        tab.NotifyUserNavigation("https://example.com/other");

        tab.Label.Should().Be("https://example.com/other");
    }

    [TestCase("https://example.com/docs", true)]
    [TestCase("http://localhost:5173/", true)]
    [TestCase("", false)]
    [TestCase("about:blank", false)]
    [TestCase("data:text/html,hello", false)]
    [TestCase("blob:https://example.com/3f1c", false)]
    public void HasNavigablePage_IsTrueOnlyForAWebAddress(string pageUrl, bool expected)
    {
        var tab = CreateTabShowingPage(pageUrl);

        tab.HasNavigablePage.Should().Be(expected);
    }

    [TestCase("")]
    [TestCase("about:blank")]
    public void ATabWithNoPage_IsBlank(string currentUrl)
    {
        var tab = CreateTabShowingPage(currentUrl);

        tab.IsBlank.Should().BeTrue();
        tab.IsPageVisible.Should().BeFalse();
        tab.AddressText.Should().BeEmpty();
    }

    [Test]
    public void ALinkThatDownloads_WhenTheNavigationEndsFirst_NeverMovesTheAddress()
    {
        // Chromium ends the navigation it turned into a download before announcing the download.
        var tab = CreateTabShowingPage("https://example.com/");
        var addresses = RecordAddresses(tab);

        tab.NotifyNavigationStarted("https://example.com/file.zip");
        tab.NotifyNavigationCompleted(NavigationOutcome.Aborted);
        tab.NotifyDownloadStarted();

        addresses.Should().BeEmpty();
        tab.IsPageVisible.Should().BeTrue();
    }

    [Test]
    public void ALinkThatDownloads_WhenTheDownloadIsAnnouncedFirst_NeverMovesTheAddress()
    {
        // WebKit announces the download before it ends the navigation the download replaced.
        var tab = CreateTabShowingPage("https://example.com/");
        var addresses = RecordAddresses(tab);

        tab.NotifyNavigationStarted("https://example.com/file.zip");
        tab.NotifyDownloadStarted();
        tab.NotifyNavigationCompleted(NavigationOutcome.Aborted);

        addresses.Should().BeEmpty();
        tab.IsPageVisible.Should().BeTrue();
    }

    [Test]
    public void ALinkToAnotherPage_MovesTheAddressOnceThePageCommits()
    {
        var tab = CreateTabShowingPage("https://example.com/");

        tab.NotifyNavigationStarted("https://example.com/next");

        tab.CurrentUrl.Should().Be("https://example.com/");

        tab.NotifyNavigationCommitted("https://example.com/next");
        tab.NotifyNavigationCompleted(NavigationOutcome.Loaded);

        tab.CurrentUrl.Should().Be("https://example.com/next");
    }

    [Test]
    public void AnAddressTheUserChose_ShowsAtOnce_AndGivesWayToTheDownloadItBecomes()
    {
        var tab = CreateTabShowingPage("https://example.com/");

        tab.NotifyUserNavigation("https://example.com/file.zip");

        tab.CurrentUrl.Should().Be("https://example.com/file.zip");

        tab.NotifyNavigationStarted("https://example.com/file.zip");
        tab.NotifyDownloadStarted();
        tab.NotifyNavigationCompleted(NavigationOutcome.Aborted);

        tab.CurrentUrl.Should().Be("https://example.com/");
    }

    [Test]
    public void AFailedNavigation_NamesTheAddressThatFailed_UntilTheNextOneStarts()
    {
        var tab = CreateTabShowingPage("https://example.com/");

        tab.NotifyNavigationStarted("https://unreachable.example/");
        tab.NotifyNavigationCompleted(NavigationOutcome.Failed);

        tab.CurrentUrl.Should().Be("https://unreachable.example/");
        tab.HasNavigationFailed.Should().BeTrue();

        // The next navigation uncovers the page behind the failure, and the bar names that page again.
        tab.NotifyNavigationStarted("https://example.com/");

        tab.CurrentUrl.Should().Be("https://example.com/");
        tab.IsPageVisible.Should().BeTrue();
    }

    [Test]
    public void APageFoundEmptyAfterItLoaded_KeepsItsAddress()
    {
        // The loaded page is probed after its navigation has completed.
        var tab = CreateTabShowingPage("https://example.com/");

        tab.NotifyNavigationStarted("https://example.com/empty");
        tab.NotifyNavigationCommitted("https://example.com/empty");
        tab.NotifyNavigationCompleted(NavigationOutcome.Loaded);
        tab.NotifyLoadedEmpty();

        tab.CurrentUrl.Should().Be("https://example.com/empty");
        tab.HasNavigationFailed.Should().BeTrue();
    }

    [Test]
    public void ANewWindowStillBlank_KeepsTheDocumentAreaAndReportsNoFailure()
    {
        // A window opened empty stays blank until the opener fills the page.
        var tab = BrowserTabViewModel.CreateForNewWindow(BlankLabel, string.Empty, opener: null);

        tab.NotifyLoadedEmpty();

        tab.HasNavigationFailed.Should().BeFalse();
        tab.IsPageVisible.Should().BeTrue();
        tab.IsBlank.Should().BeTrue();
        tab.Label.Should().Be(BlankLabel);
    }

    [Test]
    public void ANewWindow_NamesItsDestinationUntilThePageCommits()
    {
        var tab = BrowserTabViewModel.CreateForNewWindow(BlankLabel, "https://example.com/signin", opener: null);

        tab.Label.Should().Be("https://example.com/signin");
        tab.AddressText.Should().BeEmpty();
    }

    [Test]
    public void ClearingANewWindowsAddress_GivesTheDocumentAreaToThePlaceholder()
    {
        var tab = BrowserTabViewModel.CreateForNewWindow(BlankLabel, string.Empty, opener: null);

        tab.NotifyUserNavigation("about:blank");

        tab.IsPageVisible.Should().BeFalse();
    }

    [Test]
    public void CommittingAnotherPage_ClearsTheFavicon()
    {
        var tab = CreateTabShowingPage("https://example.com/");
        tab.Favicon = new byte[] { 1 };

        tab.NotifyNavigationCommitted("https://example.org/");

        tab.Favicon.Should().BeNull();
    }

    [TestCase("https://example.com/next")]
    [TestCase("https://example.com/#section")]
    public void CommittingAnotherPageOnTheSameSite_KeepsTheFavicon(string nextUrl)
    {
        // The platform does not report a new favicon for a page with the same icon. The platform also does not report
        // one when a script changes the page.
        var favicon = new byte[] { 1 };
        var tab = CreateTabShowingPage("https://example.com/");
        tab.Favicon = favicon;

        tab.NotifyNavigationCommitted(nextUrl);

        tab.Favicon.Should().BeSameAs(favicon);
    }

    [Test]
    public void AnAddressTheUserChoseOnTheSameSite_KeepsTheFavicon()
    {
        var favicon = new byte[] { 1 };
        var tab = CreateTabShowingPage("https://example.com/");
        tab.Favicon = favicon;

        tab.NotifyUserNavigation("https://example.com/next");

        tab.Favicon.Should().BeSameAs(favicon);
    }

    [Test]
    public void AnAddressTheUserChoseOnAnotherSite_ClearsTheFavicon()
    {
        var tab = CreateTabShowingPage("https://example.com/");
        tab.Favicon = new byte[] { 1 };

        tab.NotifyUserNavigation("https://example.org/");

        tab.Favicon.Should().BeNull();
    }

    [Test]
    public void CommittingARestoredTabsOwnPage_KeepsTheCachedFavicon()
    {
        // A restored tab shows the cached icon while its page loads for the first time.
        var favicon = new byte[] { 1 };
        var tab = BrowserTabViewModel.Create(BlankLabel, "https://example.com/");
        tab.Favicon = favicon;

        tab.NotifyNavigationCommitted("https://example.com/");

        tab.Favicon.Should().BeSameAs(favicon);
    }
}
