using Celbridge.Browser.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Browser.ViewModels;

/// <summary>
/// How a navigation ended.
/// </summary>
public enum NavigationOutcome
{
    /// <summary>
    /// The page loaded.
    /// </summary>
    Loaded,

    /// <summary>
    /// The page could not be loaded, and the document says so in place of the page.
    /// </summary>
    Failed,

    /// <summary>
    /// The browser abandoned the navigation before it produced a page. The page that was showing is still showing.
    /// </summary>
    Aborted
}

/// <summary>
/// One tab of a browser document: the page the tab shows, where its navigation is heading, and how the tab bar names
/// the page.
/// </summary>
public partial class BrowserTabViewModel : ObservableObject
{
    private const string BlankPageUrl = "about:blank";

    private readonly string _blankLabel;

    // Set by the stop gesture, and cleared when a navigation starts. The canceled navigation after a stop then counts
    // as stopped, with no failed load to report.
    private bool _navigationStoppedByUser;

    // Set for a new window. Its opener may fill the page with script before anything commits, and the page shows in the
    // meantime. Cleared when the user sends the tab to an address.
    private bool _showsBlankPage;

    // The address the page last committed to, which names the page on screen during a navigation. Empty until a page
    // commits.
    private string _committedUrl = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(HasNavigablePage))]
    [NotifyPropertyChangedFor(nameof(AddressText))]
    [NotifyPropertyChangedFor(nameof(IsBlank))]
    [NotifyPropertyChangedFor(nameof(IsPageVisible))]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _currentUrl = string.Empty;

    // Reported by the WebView when a navigation does not complete, which leaves the previous page on screen.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPageVisible))]
    private bool _hasNavigationFailed;

    [ObservableProperty]
    private bool _isNavigating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isShown;

    // The page's icon as encoded image bytes, or null for a page with none. The tab shows a default icon until then.
    [ObservableProperty]
    private byte[]? _favicon;

    public BrowserTabViewModel(string blankLabel)
    {
        _blankLabel = blankLabel;
    }

    /// <summary>
    /// The address the tab's icon came from. Empty for an icon from a bookmark and for a tab with no icon. The editor
    /// state saves this address, and a restored tab reads the cached icon for it.
    /// </summary>
    public string FaviconAddress { get; set; } = string.Empty;

    /// <summary>
    /// The tab that asked for this tab's page, or null for a tab the user opened. The link goes when the opener's tab
    /// closes.
    /// </summary>
    public BrowserTabViewModel? Opener { get; set; }

    /// <summary>
    /// Returns true for a tab a page's new window request opened.
    /// </summary>
    public bool IsNewWindow { get; private init; }

    /// <summary>
    /// Where the navigation in flight is heading, or empty when none is. A failed navigation puts this address in the
    /// URL bar. A tab with no page shows the address as the tab's label.
    /// </summary>
    public string NavigationDestination { get; private set; } = string.Empty;

    /// <summary>
    /// Returns true once the tab has committed to a page.
    /// </summary>
    public bool HasCommittedPage => IsPageUrl(_committedUrl);

    /// <summary>
    /// Returns true when the tab has a page address. A page that failed to load still has one.
    /// </summary>
    public bool HasPage => IsPageUrl(CurrentUrl);

    /// <summary>
    /// Returns true when the page has a web address, the kind a bookmark can name.
    /// </summary>
    public bool HasNavigablePage => BrowserUrlHelper.TryNormalize(CurrentUrl, out _);

    /// <summary>
    /// The address for the URL bar. Blank for a tab with no page.
    /// </summary>
    public string AddressText => HasPage ? CurrentUrl : string.Empty;

    /// <summary>
    /// Returns true for a tab with no page to restore or show.
    /// </summary>
    public bool IsBlank => !HasPage;

    /// <summary>
    /// Returns true when the tab's page belongs in the document area. A tab with no page and a page that failed to
    /// load both give way to the placeholder.
    /// </summary>
    public bool IsPageVisible => !HasNavigationFailed && (HasPage || _showsBlankPage);

    /// <summary>
    /// The text on the tab: the page's title, or the address until the page has a title.
    /// </summary>
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            if (HasPage)
            {
                return CurrentUrl;
            }

            if (IsPageUrl(NavigationDestination))
            {
                return NavigationDestination;
            }

            return _blankLabel;
        }
    }

    /// <summary>
    /// Creates a tab for an address the user chose, or a blank tab for an empty address.
    /// </summary>
    public static BrowserTabViewModel Create(string blankLabel, string url = "", string title = "")
    {
        var tab = new BrowserTabViewModel(blankLabel);

        if (url.Length > 0)
        {
            tab.NotifyUserNavigation(url);
        }

        tab.Title = title;

        return tab;
    }

    /// <summary>
    /// Creates a tab for a page's new window request.
    /// </summary>
    public static BrowserTabViewModel CreateForNewWindow(string blankLabel, string url, BrowserTabViewModel? opener)
    {
        var tab = new BrowserTabViewModel(blankLabel)
        {
            IsNewWindow = true,
            Opener = opener,
            NavigationDestination = url,
            _showsBlankPage = true
        };

        return tab;
    }

    /// <summary>
    /// Records that the tab is opening an address the user chose. The address bar names the destination at once, and
    /// any failure a previous navigation reported is cleared.
    /// </summary>
    public void NotifyUserNavigation(string url)
    {
        NavigationDestination = url;
        HasNavigationFailed = false;
        Title = string.Empty;
        ClearFaviconIfSiteChanges(url);
        _showsBlankPage = false;
        CurrentUrl = url;
        OnPropertyChanged(nameof(IsPageVisible));
    }

    /// <summary>
    /// Records that a navigation has begun, clearing any failure the previous one reported. The address bar goes on
    /// naming the page on screen until the navigation commits. A link that turns out to be a download therefore never
    /// shows in the address bar.
    /// </summary>
    public void NotifyNavigationStarted(string destination)
    {
        // The placeholder gives way to the page behind it, and the bar names that page again.
        if (HasNavigationFailed)
        {
            CurrentUrl = _committedUrl;
        }

        if (destination.Length > 0)
        {
            NavigationDestination = destination;
        }

        _navigationStoppedByUser = false;
        HasNavigationFailed = false;
        IsNavigating = true;
    }

    /// <summary>
    /// Records the address the page has committed to. The address bar follows the page, unless it names a failed load
    /// the placeholder is reporting.
    /// </summary>
    public void NotifyNavigationCommitted(string url)
    {
        ClearFaviconIfSiteChanges(url);

        _committedUrl = url;

        if (HasNavigationFailed)
        {
            return;
        }

        CurrentUrl = url;
    }

    /// <summary>
    /// Records that the navigation in flight became a download. The page on screen stays, so the address bar goes back
    /// to naming that page.
    /// </summary>
    public void NotifyDownloadStarted()
    {
        if (HasNavigationFailed)
        {
            return;
        }

        CurrentUrl = _committedUrl;
    }

    /// <summary>
    /// Records that the user stopped the navigation in flight. The cancellation reported for that navigation then counts
    /// as stopped, with no failed load to report.
    /// </summary>
    public void NotifyNavigationStopped()
    {
        _navigationStoppedByUser = true;
    }

    /// <summary>
    /// Records how a navigation ended. A stopped navigation keeps whatever it had rendered so far. A failed one is
    /// reported in place of the page, and the address bar names the address that failed.
    /// </summary>
    public void NotifyNavigationCompleted(NavigationOutcome outcome)
    {
        IsNavigating = false;

        var destination = NavigationDestination;
        NavigationDestination = string.Empty;
        OnPropertyChanged(nameof(Label));

        if (outcome != NavigationOutcome.Failed
            || _navigationStoppedByUser)
        {
            return;
        }

        if (destination.Length > 0)
        {
            CurrentUrl = destination;
        }

        HasNavigationFailed = true;
    }

    /// <summary>
    /// Records a page that loaded with nothing in it. A page with an address reports a failed load. A blank page reports
    /// nothing, because a new window often stays blank until the opener fills the page.
    /// </summary>
    public void NotifyLoadedEmpty()
    {
        if (!HasPage)
        {
            return;
        }

        NotifyNavigationCompleted(NavigationOutcome.Failed);
    }

    // Another site has another icon, and the tab learns that icon once the page has loaded. Pages on one site keep the
    // icon until the new page reports its own. The platform reports no icon when a script changes the page without
    // loading a new one.
    private void ClearFaviconIfSiteChanges(string url)
    {
        if (_committedUrl.Length == 0
            || IsSameSite(url, _committedUrl))
        {
            return;
        }

        Favicon = null;
        FaviconAddress = string.Empty;
    }

    private static bool IsSameSite(string url, string otherUrl)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && Uri.TryCreate(otherUrl, UriKind.Absolute, out var otherUri)
            && string.Equals(uri.Host, otherUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    // A blank WebView reports an empty source or about:blank. Neither is a page the user can reload or open in the
    // system browser.
    private static bool IsPageUrl(string url)
    {
        return !string.IsNullOrEmpty(url)
            && url != BlankPageUrl;
    }
}
