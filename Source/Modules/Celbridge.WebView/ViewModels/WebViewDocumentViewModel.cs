using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Server;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebView.Helpers;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.ViewModels;

/// <summary>
/// The lifecycle stage of the URL bar's download indicator.
/// </summary>
public enum WebViewDownloadStatus
{
    None,
    InProgress,
    Succeeded,
    Failed,
}

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IServerService _serverService;
    private readonly IIconService _iconService;
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
    [NotifyPropertyChangedFor(nameof(IsUrlBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsAddressHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsHintVisible))]
    private bool _showUrlBar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookmarksBarVisible))]
    private bool _showBookmarksBar = true;

    // Cleared when a navigation starts and set by the stop gesture, so the cancelled navigation that
    // follows is not reported as a page that failed to load.
    private bool _navigationStoppedByUser;

    // The URL bar acts on a page that is not on screen while the settings are showing, so every control
    // that would navigate is driven from this as well as from its own state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsBackEnabled))]
    [NotifyPropertyChangedFor(nameof(IsForwardEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsAddressHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsHintVisible))]
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
    [NotifyPropertyChangedFor(nameof(AddressText))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsAddressHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    [NotifyPropertyChangedFor(nameof(CanAddBookmarkFromCurrentPage))]
    private string _currentUrl = string.Empty;

    // Reported by the WebView when a navigation does not complete, which leaves the page being left
    // still rendered.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(IsAddressHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsHintVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    private bool _hasNavigationFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadIconVisible))]
    private bool _isNavigating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadIndicatorVisible))]
    [NotifyPropertyChangedFor(nameof(IsDownloadInProgress))]
    [NotifyPropertyChangedFor(nameof(IsDownloadSucceeded))]
    [NotifyPropertyChangedFor(nameof(IsDownloadFailed))]
    private WebViewDownloadStatus _downloadStatus = WebViewDownloadStatus.None;

    /// <summary>
    /// The imported resource of the most recent completed download. Clicking the
    /// settled download indicator reveals it in the Explorer.
    /// </summary>
    public ResourceKey LastDownloadedResource { get; private set; } = ResourceKey.Empty;

    private WebViewDocumentRole _role;

    /// <summary>
    /// Selects how LoadContent and NavigateUrl interpret the backing resource. Set
    /// by the view before the first LoadContent call. Defaults to ExternalUrl, which
    /// matches the .webview document behaviour assumed by the parameterless code-gen flow.
    /// </summary>
    public WebViewDocumentRole Role
    {
        get => _role;
        set
        {
            _role = value;
            OnPropertyChanged(nameof(IsUrlBarVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(IsBackEnabled));
            OnPropertyChanged(nameof(IsForwardEnabled));
            OnPropertyChanged(nameof(IsReloadOrStopEnabled));
            OnPropertyChanged(nameof(IsPlaceholderVisible));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsLoadFailedVisible));
            OnPropertyChanged(nameof(IsAddressHintVisible));
            OnPropertyChanged(nameof(IsSettingsHintVisible));
            OnPropertyChanged(nameof(IsPageOnScreen));
            OnPropertyChanged(nameof(IsBookmarksBarVisible));
        }
    }

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
    /// True when the browser-style URL bar should be shown: the external-URL role
    /// only, and only while the document does not hide it via show_url_bar.
    /// </summary>
    public bool IsUrlBarVisible => Role == WebViewDocumentRole.ExternalUrl && ShowUrlBar;

    /// <summary>
    /// True when the settings should take the document area in place of the page. Like the URL bar, the
    /// settings are external-URL chrome and never appear for the HTML viewer.
    /// </summary>
    public bool IsSettingsVisible => Role == WebViewDocumentRole.ExternalUrl && IsSettingsOpen;

    /// <summary>
    /// True when the bookmarks bar should be shown. It stays up while the settings have the document area,
    /// where it doubles as a live preview of the bookmarks being edited.
    /// </summary>
    public bool IsBookmarksBarVisible => Role == WebViewDocumentRole.ExternalUrl
        && ShowBookmarksBar
        && Bookmarks.Any(bookmark => bookmark.IsNavigable);

    /// <summary>
    /// The bookmarks the bar offers a button for: those that can actually be navigated to, so an entry
    /// still being filled in does not put a button there that does nothing.
    /// </summary>
    public IReadOnlyList<WebViewBookmarkViewModel> ToolbarBookmarks =>
        Bookmarks.Where(bookmark => bookmark.IsNavigable).ToList();

    /// <summary>
    /// True when the document is showing a page, as opposed to nothing or a page that failed to load.
    /// </summary>
    public bool HasPage => IsPageUrl(CurrentUrl);

    /// <summary>
    /// The address as the URL bar should show it. Blank for a document with no page, so clearing the
    /// address and committing it leaves the bar empty rather than naming the blank page behind it.
    /// </summary>
    public string AddressText => HasPage ? CurrentUrl : string.Empty;

    /// <summary>
    /// True when the placeholder takes the document area in place of a page: the document has none to
    /// show, or the one it was sent to did not load.
    /// </summary>
    public bool IsPlaceholderVisible => Role == WebViewDocumentRole.ExternalUrl
        && !IsSettingsVisible
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
    /// True when the placeholder should point at the URL bar, which is where a document showing it opens
    /// a page.
    /// </summary>
    public bool IsAddressHintVisible => IsEmptyStateVisible && ShowUrlBar;

    /// <summary>
    /// True when the placeholder should point at the settings instead, the document having no URL bar to
    /// type an address into.
    /// </summary>
    public bool IsSettingsHintVisible => IsEmptyStateVisible && !ShowUrlBar;

    /// <summary>
    /// True when the page is what fills the document area, rather than the settings or the placeholder.
    /// </summary>
    public bool IsPageOnScreen => !IsSettingsVisible && !IsPlaceholderVisible;

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
    public bool CanSetCurrentPageAsHome => IsPageUrl(CurrentUrl) && CurrentUrl != SourceUrl;

    /// <summary>
    /// True when the page on screen can be bookmarked, which a page a bookmark already points at cannot.
    /// </summary>
    public bool CanAddBookmarkFromCurrentPage => IsPageUrl(CurrentUrl) && !IsCurrentPageBookmarked;

    private bool IsCurrentPageBookmarked
    {
        get
        {
            foreach (var bookmark in Bookmarks)
            {
                if (WebViewUrlHelper.IsSameUrl(bookmark.Url, CurrentUrl))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool CanReload => IsPageUrl(CurrentUrl);

    /// <summary>
    /// True when the page can be navigated back to the previous entry in its history.
    /// </summary>
    public bool IsBackEnabled => CanGoBack && !IsSettingsVisible;

    /// <summary>
    /// True when the page can be navigated forward to the next entry in its history.
    /// </summary>
    public bool IsForwardEnabled => CanGoForward && !IsSettingsVisible;

    /// <summary>
    /// True when the page can be navigated to the configured Home URL.
    /// </summary>
    public bool IsHomeEnabled => IsHomeUrlValid;

    public bool IsReloadOrStopEnabled => (IsNavigating || CanReload) && !IsSettingsVisible;

    /// <summary>
    /// True when the reload/stop button shows the reload icon; while a navigation
    /// is in flight it shows the stop icon instead.
    /// </summary>
    public bool IsReloadIconVisible => !IsNavigating;

    public bool CanOpenInBrowser => IsPageUrl(CurrentUrl);

    public bool IsDownloadIndicatorVisible => DownloadStatus != WebViewDownloadStatus.None;

    public bool IsDownloadInProgress => DownloadStatus == WebViewDownloadStatus.InProgress;

    public bool IsDownloadSucceeded => DownloadStatus == WebViewDownloadStatus.Succeeded;

    public bool IsDownloadFailed => DownloadStatus == WebViewDownloadStatus.Failed;

    /// <summary>
    /// The URL the view should navigate to. For .webview documents this is the configured source URL
    /// verbatim. For the HTML viewer it is the loopback /project/ URL on the Skia heads, or the project
    /// virtual-host URL on Windows.
    /// </summary>
    public string NavigateUrl
    {
        get
        {
            if (Role == WebViewDocumentRole.HtmlViewer)
            {
                if (FileResource.IsEmpty)
                {
                    return string.Empty;
                }

                // URL path is the bare resource path. The "project:" prefix that
                // ResourceKey.ToString() emits is for serialised diagnostics,
                // not URL construction.

                // Served over the loopback file server's /project/ route. Relative asset
                // references in the HTML resolve against this origin.
                return $"http://127.0.0.1:{_serverService.Port}/project/{FileResource.Path}";
            }

            return SourceUrl;
        }
    }

    // Code gen requires a parameterless constructor
    public WebViewDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebViewDocumentViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IServerService serverService,
        IIconService iconService,
        IStringLocalizer stringLocalizer)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _serverService = serverService;
        _iconService = iconService;
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
        if (Role == WebViewDocumentRole.HtmlViewer)
        {
            // HTML viewer content is served by the file server (loopback /project/ route, or the project
            // virtual host on Windows). Nothing to parse. Succeeding here lets TryNavigate run.
            await Task.CompletedTask;
            return Result.Ok();
        }

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
        // Don't immediately try to save again if the save fails.
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

        return await SaveTextToFileAsync(content.ToToml());
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
    /// Records that a navigation has begun, clearing the failure the previous one may have reported.
    /// </summary>
    public void NotifyNavigationStarted()
    {
        _navigationStoppedByUser = false;
        HasNavigationFailed = false;
        IsNavigating = true;
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
    /// Records how a navigation ended. A stopped navigation keeps whatever it had rendered so far.
    /// </summary>
    public void NotifyNavigationCompleted(bool isSuccess)
    {
        IsNavigating = false;

        if (isSuccess
            || _navigationStoppedByUser)
        {
            return;
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
    /// Adds a bookmark for the page currently on screen, named after its host so the button reads as
    /// something before the user renames it.
    /// </summary>
    public void AddBookmarkFromCurrentPage()
    {
        if (!TryNormalizeUserUrl(CurrentUrl, out var pageUrl))
        {
            return;
        }

        var name = string.Empty;
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            name = uri.Host;
        }

        var bookmark = CreateBookmark(new WebViewBookmark(pageUrl, name));
        Bookmarks.Add(bookmark);
    }

    /// <summary>
    /// Builds a bookmark for this document. Use it for every bookmark the settings add, so each one is
    /// wired to record its edits against the document.
    /// </summary>
    public WebViewBookmarkViewModel CreateBookmark(WebViewBookmark bookmark)
    {
        var bookmarkViewModel = new WebViewBookmarkViewModel(_iconService, _stringLocalizer)
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

    public void BeginDownload()
    {
        DownloadStatus = WebViewDownloadStatus.InProgress;
    }

    public void CompleteDownload(ResourceKey importedResource)
    {
        LastDownloadedResource = importedResource;
        DownloadStatus = WebViewDownloadStatus.Succeeded;
    }

    public void FailDownload()
    {
        DownloadStatus = WebViewDownloadStatus.Failed;
    }

    public void ClearDownloadIndicator()
    {
        DownloadStatus = WebViewDownloadStatus.None;
    }

    /// <summary>
    /// Reveals the most recent completed download in the Explorer panel.
    /// </summary>
    public void RevealLastDownload()
    {
        if (LastDownloadedResource.IsEmpty)
        {
            return;
        }

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = LastDownloadedResource;
            command.ShowExplorerPanel = true;
        });
    }

    protected override IResourceFileSystem GetFileSystem()
    {
        // Route the base-class load and save helpers through the injected wrapper
        // so tests can substitute the file system without a service locator.
        return _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    }

    // Records an edit against the document, unless the change came from the load rather than from the user.
    // An HTML viewer has no .webview file behind it, so nothing it reports is a change to write back.
    private void RecordDataChanged()
    {
        if (_isLoadingContent
            || Role == WebViewDocumentRole.HtmlViewer)
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
            }
        }

        OnPropertyChanged(nameof(IsBookmarksBarVisible));
        OnPropertyChanged(nameof(ToolbarBookmarks));
        OnPropertyChanged(nameof(CanAddBookmarkFromCurrentPage));

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
        }

        RecordDataChanged();
    }

    // A blank WebView reports an empty source or about:blank; neither is a page
    // the user can reload or open in the system browser.
    private static bool IsPageUrl(string url)
    {
        return !string.IsNullOrEmpty(url) &&
               url != "about:blank";
    }
}
