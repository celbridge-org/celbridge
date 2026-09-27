using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebView.Helpers;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.ViewModels;

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
    /// The page could not be loaded, and the document says so in place of it.
    /// </summary>
    Failed,

    /// <summary>
    /// The browser abandoned the navigation before it produced a page. The page that was showing is still
    /// showing, so there is nothing to report.
    /// </summary>
    Aborted
}

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private const string WwwPrefix = "www.";

    private readonly ILogger<WebViewDocumentViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;

    // Set while the document's settings are being read off disk, so the bookmarks arriving in the
    // collection are not taken for edits and written straight back out.
    private bool _isLoadingContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeUrlValid))]
    [NotifyPropertyChangedFor(nameof(IsHomeUrlInvalid))]
    [NotifyPropertyChangedFor(nameof(IsHomeEnabled))]
    [NotifyPropertyChangedFor(nameof(HomeUrlTooltip))]
    [NotifyPropertyChangedFor(nameof(CanSetCurrentPageAsHome))]
    private string _sourceUrl = string.Empty;

    [ObservableProperty]
    private bool _showUrlBar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookmarksBarVisible))]
    private bool _showBookmarksBar = true;

    // Cleared when a navigation starts and set by the stop gesture, so the cancelled navigation that
    // follows is not reported as a page that failed to load.
    private bool _navigationStoppedByUser;

    // The address the page last committed to, which names the page on screen whatever navigation is in
    // flight. Empty until a page commits.
    private string _committedUrl = string.Empty;

    // The URL bar acts on a page that is not on screen while the settings are showing, so every control
    // that would navigate is driven from this as well as from its own state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackEnabled))]
    [NotifyPropertyChangedFor(nameof(IsForwardEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    private bool _isSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackEnabled))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForwardEnabled))]
    private bool _canGoForward;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReload))]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(CanOpenInBrowser))]
    [NotifyPropertyChangedFor(nameof(CanSetCurrentPageAsHome))]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(HasNavigablePage))]
    [NotifyPropertyChangedFor(nameof(AddressText))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    [NotifyPropertyChangedFor(nameof(CanAddBookmarkFromCurrentPage))]
    [NotifyPropertyChangedFor(nameof(IsCurrentPageBookmarked))]
    private string _currentUrl = string.Empty;

    // Reported by the WebView when a navigation does not complete, which leaves the previous page on
    // screen.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    private bool _hasNavigationFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadIconVisible))]
    private bool _isNavigating;

    /// <summary>
    /// The document's bookmarks, in the order their buttons appear in the bookmarks bar. Editing the
    /// collection or any bookmark in it records a change against the document.
    /// </summary>
    public ObservableCollection<WebViewBookmarkViewModel> Bookmarks { get; } = new();

    /// <summary>
    /// Raised when something other than the URL bar asks the document to open a page, carrying the URL to
    /// navigate to. The view owns the WebView, so it performs the navigation.
    /// </summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>
    /// Where the navigation in flight is heading, or empty when none is. The address bar names it only when
    /// the user chose it through the document, or when it fails and the placeholder reports on it.
    /// </summary>
    public string NavigationDestination { get; private set; } = string.Empty;

    /// <summary>
    /// True when the bookmarks bar should be shown. It stays up while the settings have the document area,
    /// where it doubles as a live preview of the bookmarks being edited.
    /// </summary>
    public bool IsBookmarksBarVisible => ShowBookmarksBar
        && Bookmarks.Any(bookmark => bookmark.IsNavigable);

    /// <summary>
    /// The bookmarks the bar offers a button for: those that can actually be navigated to, so an entry
    /// still being filled in does not put a button there that does nothing.
    /// </summary>
    public IReadOnlyList<WebViewBookmarkViewModel> ToolbarBookmarks =>
        Bookmarks.Where(bookmark => bookmark.IsNavigable).ToList();

    /// <summary>
    /// True when the document has a page address, as opposed to showing nothing. A page that failed to load still
    /// has one.
    /// </summary>
    public bool HasPage => IsPageUrl(CurrentUrl);

    /// <summary>
    /// True when the page has a web address, the kind a bookmark or the Home URL can hold.
    /// </summary>
    public bool HasNavigablePage => TryNormalizeUserUrl(CurrentUrl, out _);

    /// <summary>
    /// The address as the URL bar should show it. Blank for a document with no page, so clearing the
    /// address and committing it leaves the bar empty rather than naming the blank page behind it.
    /// </summary>
    public string AddressText => HasPage ? CurrentUrl : string.Empty;

    /// <summary>
    /// True when the placeholder takes the document area in place of a page: the document has none to
    /// show, or the one it was sent to did not load.
    /// </summary>
    public bool IsPlaceholderVisible => !IsSettingsOpen
        && (HasNavigationFailed || !HasPage);

    /// <summary>
    /// True when the placeholder should say how to open a page, the document having none.
    /// </summary>
    public bool IsEmptyStateVisible => IsPlaceholderVisible && !HasNavigationFailed;

    /// <summary>
    /// True when the placeholder should report that the address it was sent to did not load.
    /// </summary>
    public bool IsLoadFailedVisible => IsPlaceholderVisible && HasNavigationFailed;

    /// <summary>
    /// True when the document offers a way to open a page without going through its settings.
    /// </summary>
    public bool HasWayToNavigate => !string.IsNullOrWhiteSpace(SourceUrl)
        || ShowUrlBar
        || IsBookmarksBarVisible;

    /// <summary>
    /// True when the page is what fills the document area, rather than the settings or the placeholder.
    /// </summary>
    public bool IsPageOnScreen => !IsSettingsOpen && !IsPlaceholderVisible;

    /// <summary>
    /// True when the configured Home URL is a navigable external URL.
    /// </summary>
    public bool IsHomeUrlValid => TryNormalizeUserUrl(SourceUrl, out _);

    /// <summary>
    /// True when the user has entered a Home URL that cannot be navigated to. A blank
    /// Home URL is unconfigured rather than wrong, so it does not report as invalid.
    /// </summary>
    public bool IsHomeUrlInvalid => !string.IsNullOrWhiteSpace(SourceUrl) && !IsHomeUrlValid;

    /// <summary>
    /// The whole Home URL, for a hover over the address box that shows it. Null when there is no URL
    /// configured, so an empty box raises no tooltip at all.
    /// </summary>
    public string? HomeUrlTooltip
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceUrl))
            {
                return null;
            }

            return SourceUrl;
        }
    }

    /// <summary>
    /// True when the page on screen is somewhere other than the configured Home URL,
    /// so adopting it as the new Home URL would change something.
    /// </summary>
    public bool CanSetCurrentPageAsHome => HasNavigablePage && !WebViewUrlHelper.IsSameUrl(CurrentUrl, SourceUrl);

    /// <summary>
    /// True when the page on screen can be bookmarked, which a page a bookmark already points at cannot.
    /// </summary>
    public bool CanAddBookmarkFromCurrentPage => HasNavigablePage && !IsCurrentPageBookmarked;

    /// <summary>
    /// True when a bookmark already points at the page on screen.
    /// </summary>
    public bool IsCurrentPageBookmarked => FindBookmarkForCurrentPage() is not null;

    public bool CanReload => IsPageUrl(CurrentUrl);

    /// <summary>
    /// True when the page can be navigated back to the previous entry in its history.
    /// </summary>
    public bool IsBackEnabled => CanGoBack && !IsSettingsOpen;

    /// <summary>
    /// True when the page can be navigated forward to the next entry in its history.
    /// </summary>
    public bool IsForwardEnabled => CanGoForward && !IsSettingsOpen;

    /// <summary>
    /// True when the page can be navigated to the configured Home URL.
    /// </summary>
    public bool IsHomeEnabled => IsHomeUrlValid;

    public bool IsReloadOrStopEnabled => (IsNavigating || CanReload) && !IsSettingsOpen;

    /// <summary>
    /// True when the reload/stop button shows the reload icon; while a navigation
    /// is in flight it shows the stop icon instead.
    /// </summary>
    public bool IsReloadIconVisible => !IsNavigating;

    public bool CanOpenInBrowser => IsPageUrl(CurrentUrl);

    // Code gen requires a parameterless constructor
    public WebViewDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebViewDocumentViewModel(
        ILogger<WebViewDocumentViewModel> logger,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;

        PropertyChanged += WebViewDocumentViewModel_PropertyChanged;
        Bookmarks.CollectionChanged += Bookmarks_CollectionChanged;
    }

    // The Home URL field takes the same shorthand the address bar does, so a host typed without a scheme is
    // completed as it is entered rather than written to the file as something the loader would refuse.
    partial void OnSourceUrlChanged(string value)
    {
        if (!WebViewUrlHelper.TryNormalize(value, out var normalizedUrl)
            || normalizedUrl == value)
        {
            return;
        }

        SourceUrl = normalizedUrl;
    }

    public async Task<Result> LoadContent()
    {
        // A reload after a rename re-enters here, so the parsed values are pushed onto the properties with
        // the change handlers held off.
        _isLoadingContent = true;

        try
        {
            var loadResult = await LoadDocumentSettingsAsync();
            if (loadResult.IsFailure)
            {
                return loadResult;
            }
        }
        finally
        {
            _isLoadingContent = false;
        }

        return Result.Ok();
    }

    private async Task<Result> LoadDocumentSettingsAsync()
    {
        // The .webview file is a small TOML document that carries the configured
        // external URL and chrome settings. Read via the gateway so the load picks
        // up the same containment validation as writes.
        var resourceFileSystem = GetFileSystem();

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            // No file on disk yet (e.g. just created via the Add File dialog).
            // Treat as a blank URL so the view shows nothing rather than failing.
            SourceUrl = string.Empty;
            ShowUrlBar = true;
            ShowBookmarksBar = true;
            PopulateBookmarks(Array.Empty<WebViewBookmark>());
            return Result.Ok();
        }

        var readResult = await LoadTextFromFileAsync();
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(readResult);
        }

        var parseResult = WebViewFileContent.TryParse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return Result.Fail($"Failed to parse '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(parseResult);
        }
        var content = parseResult.Value;

        if (content.UnknownFields.Count > 0)
        {
            // Advisory: the document still opens, but ToToml drops these keys on the next save.
            _logger.LogWarning(
                $"WebView document declares keys the host does not define ({string.Join(", ", content.UnknownFields)}): {FileResource}");
        }

        ShowUrlBar = content.ShowUrlBar;
        ShowBookmarksBar = content.ShowBookmarksBar;
        PopulateBookmarks(content.Bookmarks);

        var sourceUrl = content.SourceUrl.Trim();
        if (string.IsNullOrEmpty(sourceUrl))
        {
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        // A hand-edited file may name a host with no scheme, which is the shorthand the address bar takes,
        // so it is completed here rather than failing the document over it.
        if (!WebViewUrlHelper.TryNormalize(sourceUrl, out var normalizedUrl))
        {
            return Result.Fail(
                $"{ExplorerConstants.WebViewExtension} documents only support external http/https URLs. Configured URL: '{sourceUrl}'");
        }

        SourceUrl = normalizedUrl;
        return Result.Ok();
    }

    public async Task<Result> SaveDocumentContent()
    {
        // Cleared before the write so an edit made while it is in flight is not counted as written.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        var bookmarks = Bookmarks
            .Select(bookmark => bookmark.ToBookmark())
            .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Url))
            .ToList();

        var content = new WebViewFileContent(SourceUrl, ShowUrlBar, ShowBookmarksBar)
        {
            Bookmarks = bookmarks
        };

        var saveResult = await SaveTextToFileAsync(content.ToToml());
        if (saveResult.IsFailure)
        {
            HasUnsavedChanges = true;
        }

        return saveResult;
    }

    /// <summary>
    /// Validates a user-entered address, prefixing https:// when no scheme was
    /// typed. Returns false when the input cannot become a navigable external URL.
    /// </summary>
    public bool TryNormalizeUserUrl(string input, out string url)
    {
        return WebViewUrlHelper.TryNormalize(input, out url);
    }

    public void OpenBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }

    /// <summary>
    /// Records that the document is opening an address the user chose through it: one entered in the URL
    /// bar, a bookmark, Home, or the Home URL it opens on. The address bar names the destination at once,
    /// and the failure a previous navigation may have reported is cleared.
    /// </summary>
    public void NotifyUserNavigation(string url)
    {
        NavigationDestination = url;
        HasNavigationFailed = false;
        CurrentUrl = url;
    }

    /// <summary>
    /// Records that a navigation has begun, clearing the failure the previous one may have reported. The
    /// address bar goes on naming the page on screen until the navigation commits, so a link that turns
    /// out to be a download never shows in it.
    /// </summary>
    public void NotifyNavigationStarted(string destination)
    {
        // The placeholder reporting the failure gives way to the page behind it, which the bar names again.
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
    /// Records the address the page has committed to, as a new page takes the place of the old one or the
    /// page moves to another address of its own. The address bar follows it, unless it is naming a failed
    /// load that the placeholder is reporting.
    /// </summary>
    public void NotifyNavigationCommitted(string url)
    {
        _committedUrl = url;

        if (HasNavigationFailed)
        {
            return;
        }

        CurrentUrl = url;
    }

    /// <summary>
    /// Records that the navigation in flight became a download. The page on screen stays, so the address
    /// bar goes back to naming it, having moved only if the user chose the download's address.
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
    /// Records that the user stopped the navigation in flight, so the cancellation reported for it is not
    /// taken for a page that failed to load.
    /// </summary>
    public void NotifyNavigationStopped()
    {
        _navigationStoppedByUser = true;
    }

    /// <summary>
    /// Records how a navigation ended. A stopped navigation keeps whatever it had rendered so far. A failed
    /// one is reported in place of the page, and the address bar names the address that failed.
    /// </summary>
    public void NotifyNavigationCompleted(NavigationOutcome outcome)
    {
        IsNavigating = false;

        var destination = NavigationDestination;
        NavigationDestination = string.Empty;

        if (outcome != NavigationOutcome.Failed ||
            _navigationStoppedByUser)
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
    /// Returns the document to the page. The settings need a way out of their own, because a document that
    /// hides the URL bar hides the toggle that opened them.
    /// </summary>
    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    /// <summary>
    /// Adopts the page currently on screen as the document's Home URL.
    /// </summary>
    public void SetCurrentPageAsHome()
    {
        if (!TryNormalizeUserUrl(CurrentUrl, out var homeUrl))
        {
            return;
        }

        SourceUrl = homeUrl;
    }

    /// <summary>
    /// Adopts the page a bookmark opens as the document's Home URL.
    /// </summary>
    public void SetBookmarkAsHome(WebViewBookmarkViewModel bookmark)
    {
        if (!TryNormalizeUserUrl(bookmark.Url, out var homeUrl))
        {
            return;
        }

        SourceUrl = homeUrl;
    }

    /// <summary>
    /// Adds a bookmark for the page currently on screen, named after its site.
    /// </summary>
    public WebViewBookmarkViewModel? AddBookmarkFromCurrentPage()
    {
        if (!TryNormalizeUserUrl(CurrentUrl, out var pageUrl))
        {
            return null;
        }

        var name = string.Empty;
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            name = GetDefaultBookmarkName(uri);
        }

        var bookmark = CreateBookmark(new WebViewBookmark(pageUrl, name));
        Bookmarks.Add(bookmark);

        return bookmark;
    }

    /// <summary>
    /// The bookmark pointing at the page currently on screen, or null when no bookmark does.
    /// </summary>
    public WebViewBookmarkViewModel? FindBookmarkForCurrentPage()
    {
        foreach (var bookmark in Bookmarks)
        {
            if (WebViewUrlHelper.IsSameUrl(bookmark.Url, CurrentUrl))
            {
                return bookmark;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a bookmark for this document. Use it for every bookmark the settings add, so each one is
    /// wired to record its edits against the document.
    /// </summary>
    public WebViewBookmarkViewModel CreateBookmark(WebViewBookmark bookmark)
    {
        var bookmarkViewModel = new WebViewBookmarkViewModel(_stringLocalizer)
        {
            Url = bookmark.Url,
            Name = bookmark.Name,
            Icon = bookmark.Icon
        };

        return bookmarkViewModel;
    }

    /// <summary>
    /// Opens a bookmark, leaving the settings first if they have the document area: the page a bookmark
    /// opens is behind them.
    /// </summary>
    public void OpenBookmark(WebViewBookmarkViewModel bookmark)
    {
        if (!TryNormalizeUserUrl(bookmark.Url, out var url))
        {
            return;
        }

        CloseSettings();

        NavigateRequested?.Invoke(this, url);
    }

    protected override IResourceFileSystem GetFileSystem()
    {
        // Route the base-class load and save helpers through the injected wrapper
        // so tests can substitute the file system without a service locator.
        return _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    }

    // Records an edit against the document, unless the change came from the load rather than from the user.
    private void RecordDataChanged()
    {
        if (_isLoadingContent)
        {
            return;
        }

        OnDataChanged();
    }

    private void WebViewDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceUrl) ||
            e.PropertyName == nameof(ShowUrlBar) ||
            e.PropertyName == nameof(ShowBookmarksBar))
        {
            RecordDataChanged();
        }

        if (e.PropertyName == nameof(SourceUrl))
        {
            foreach (var bookmark in Bookmarks)
            {
                UpdateHomeState(bookmark);
            }
        }
    }

    // Replaces the bookmarks with those just read off disk. The collection handler is what keeps each
    // bookmark's edits wired up, so the load goes through the collection rather than around it.
    private void PopulateBookmarks(IReadOnlyList<WebViewBookmark> bookmarks)
    {
        // Clearing raises a reset, which reports no old items, so the outgoing bookmarks are detached here.
        foreach (var bookmark in Bookmarks)
        {
            bookmark.PropertyChanged -= Bookmark_PropertyChanged;
        }

        Bookmarks.Clear();

        foreach (var bookmark in bookmarks)
        {
            var bookmarkViewModel = CreateBookmark(bookmark);
            Bookmarks.Add(bookmarkViewModel);
        }
    }

    private void Bookmarks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WebViewBookmarkViewModel bookmark in e.OldItems)
            {
                bookmark.PropertyChanged -= Bookmark_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WebViewBookmarkViewModel bookmark in e.NewItems)
            {
                bookmark.PropertyChanged += Bookmark_PropertyChanged;
                UpdateHomeState(bookmark);
            }
        }

        OnPropertyChanged(nameof(IsBookmarksBarVisible));
        OnPropertyChanged(nameof(ToolbarBookmarks));
        OnPropertyChanged(nameof(CanAddBookmarkFromCurrentPage));
        OnPropertyChanged(nameof(IsCurrentPageBookmarked));

        RecordDataChanged();
    }

    private void Bookmark_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The stored properties only. A bookmark also reports the display properties derived from these,
        // which carry no edit of their own.
        if (e.PropertyName != nameof(WebViewBookmarkViewModel.Url)
            && e.PropertyName != nameof(WebViewBookmarkViewModel.Name)
            && e.PropertyName != nameof(WebViewBookmarkViewModel.Icon))
        {
            return;
        }

        if (e.PropertyName == nameof(WebViewBookmarkViewModel.Url))
        {
            OnPropertyChanged(nameof(IsBookmarksBarVisible));
            OnPropertyChanged(nameof(ToolbarBookmarks));
            OnPropertyChanged(nameof(CanAddBookmarkFromCurrentPage));
            OnPropertyChanged(nameof(IsCurrentPageBookmarked));

            if (sender is WebViewBookmarkViewModel bookmark)
            {
                UpdateHomeState(bookmark);
            }
        }

        RecordDataChanged();
    }

    // The host a page is on, with its port when that is not the scheme's default. A leading "www." is
    // dropped unless no dot would remain.
    private static string GetDefaultBookmarkName(Uri uri)
    {
        var name = GetReadableHost(uri);
        if (!uri.IsDefaultPort)
        {
            name = $"{name}:{uri.Port}";
        }

        if (!name.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var remainder = name.Substring(WwwPrefix.Length);
        if (!remainder.Contains('.'))
        {
            return name;
        }

        return remainder;
    }

    // A web view reports an internationalized domain in its ASCII form; this turns it back into the name
    // the user reads.
    private static string GetReadableHost(Uri uri)
    {
        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return uri.Host;
        }

        try
        {
            return new IdnMapping().GetUnicode(uri.IdnHost);
        }
        catch (ArgumentException)
        {
            // An ASCII form that does not decode is named as it is.
            return uri.Host;
        }
    }

    private void UpdateHomeState(WebViewBookmarkViewModel bookmark)
    {
        bookmark.IsHome = WebViewUrlHelper.IsSameUrl(bookmark.Url, SourceUrl);
    }

    // A blank WebView reports an empty source or about:blank; neither is a page
    // the user can reload or open in the system browser.
    private static bool IsPageUrl(string url)
    {
        return !string.IsNullOrEmpty(url) &&
               url != "about:blank";
    }
}
