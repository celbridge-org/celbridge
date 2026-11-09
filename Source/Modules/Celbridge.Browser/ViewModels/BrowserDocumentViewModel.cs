using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Celbridge.Browser.Helpers;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.ViewModels;

public partial class BrowserDocumentViewModel : DocumentViewModel
{
    /// <summary>
    /// The largest number of bookmarks the empty page lists. The bookmark list shows them all.
    /// </summary>
    public const int EmptyPageBookmarkLimit = 12;

    private const string WwwPrefix = "www.";
    private const string MailAddressPrefix = "mailto:";

    // The schemes a new window's tab can show. A blob or data address names content built by the opener page.
    private static readonly string[] NewWindowTabSchemes =
    [
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "about",
        "blob",
        "data"
    ];

    private static readonly JsonSerializerOptions EditorStateSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<BrowserDocumentViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;

    // Set while the document's file is being read. The bookmarks arriving in the collection then count as loaded, and
    // nothing writes them back out.
    private bool _isLoadingContent;

    // Set once opening the document has restored the editor state or opened the Open on Start bookmarks. A save before
    // then would write empty tabs over the stored state.
    private bool _isEditorStateReady;

    // The URL bar's navigation controls would act on a hidden page while the settings show. Each control's state
    // therefore follows this flag as well.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackEnabled))]
    [NotifyPropertyChangedFor(nameof(IsForwardEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoadFailedVisible))]
    [NotifyPropertyChangedFor(nameof(AreEmptyPageBookmarksVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageOnScreen))]
    private bool _isSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreToolbarsVisible))]
    private LayoutMode _layoutMode;

    [ObservableProperty]
    private BrowserTabViewModel? _shownTab;

    /// <summary>
    /// The settings section to show when the settings open.
    /// </summary>
    public string SettingsSectionKey { get; set; } = string.Empty;

    /// <summary>
    /// Raised after ChooseTab shows a tab.
    /// </summary>
    public event EventHandler? TabChosen;

    /// <summary>
    /// The document's bookmarks, in the order the bookmark list shows them. Editing the collection or any bookmark in
    /// it records a change against the document.
    /// </summary>
    public ObservableCollection<BrowserBookmarkViewModel> Bookmarks { get; } = new();

    /// <summary>
    /// The open tabs, in tab bar order.
    /// </summary>
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new();

    /// <summary>
    /// Returns true when the URL bar and the tab bar show. Only presentation mode hides them.
    /// </summary>
    public bool AreToolbarsVisible => LayoutMode != LayoutMode.Presentation;

    /// <summary>
    /// The bookmarks the bookmark list offers, which are those with a navigable address. The list leaves out an entry
    /// while the user is still filling it in.
    /// </summary>
    public IReadOnlyList<BrowserBookmarkViewModel> NavigableBookmarks =>
        Bookmarks.Where(bookmark => bookmark.IsNavigable).ToList();

    /// <summary>
    /// The bookmarks the empty page lists: the first navigable ones up to EmptyPageBookmarkLimit, in the bookmark list's
    /// order.
    /// </summary>
    public IReadOnlyList<BrowserBookmarkViewModel> EmptyPageBookmarks =>
        NavigableBookmarks.Take(EmptyPageBookmarkLimit).ToList();

    /// <summary>
    /// The shown tab's address, or empty with no tab shown.
    /// </summary>
    public string CurrentUrl => ShownTab?.CurrentUrl ?? string.Empty;

    /// <summary>
    /// Returns true when the shown tab has a page address. A page that failed to load still has one.
    /// </summary>
    public bool HasPage => ShownTab?.HasPage ?? false;

    /// <summary>
    /// Returns true when the shown page has a web address, the kind a bookmark can name.
    /// </summary>
    public bool HasNavigablePage => ShownTab?.HasNavigablePage ?? false;

    /// <summary>
    /// The address for the URL bar, which is blank for a blank tab and for a document with zero tabs.
    /// </summary>
    public string AddressText => ShownTab?.AddressText ?? string.Empty;

    /// <summary>
    /// Returns true when the placeholder takes the document area in place of a page.
    /// </summary>
    public bool IsPlaceholderVisible => !IsSettingsOpen
        && (ShownTab is null || !ShownTab.IsPageVisible);

    /// <summary>
    /// Returns true when the placeholder says how to open a page.
    /// </summary>
    public bool IsEmptyStateVisible => IsPlaceholderVisible && !IsLoadFailedVisible;

    /// <summary>
    /// Returns true when the placeholder reports that the address the tab was sent to did not load.
    /// </summary>
    public bool IsLoadFailedVisible => IsPlaceholderVisible && (ShownTab?.HasNavigationFailed ?? false);

    /// <summary>
    /// Returns true when the placeholder lists bookmarks: for a blank tab or zero tabs, in a document with a navigable
    /// bookmark. The placeholder for a page that failed to load does not list bookmarks.
    /// </summary>
    public bool AreEmptyPageBookmarksVisible => IsEmptyStateVisible
        && Bookmarks.Any(bookmark => bookmark.IsNavigable);

    /// <summary>
    /// Returns true when the page fills the document area.
    /// </summary>
    public bool IsPageOnScreen => !IsSettingsOpen && !IsPlaceholderVisible;

    /// <summary>
    /// Returns true when the shown page can be bookmarked. A page a bookmark already points at cannot.
    /// </summary>
    public bool CanAddBookmarkFromCurrentPage => HasNavigablePage && !IsCurrentPageBookmarked;

    /// <summary>
    /// Returns true when a bookmark points at the shown page.
    /// </summary>
    public bool IsCurrentPageBookmarked => FindBookmarkForCurrentPage() is not null;

    public bool CanReload => HasPage;

    /// <summary>
    /// Returns true when the shown page can go back to the previous entry in its history.
    /// </summary>
    public bool IsBackEnabled => (ShownTab?.CanGoBack ?? false) && !IsSettingsOpen;

    /// <summary>
    /// Returns true when the shown page can go forward to the next entry in its history.
    /// </summary>
    public bool IsForwardEnabled => (ShownTab?.CanGoForward ?? false) && !IsSettingsOpen;

    public bool IsNavigating => ShownTab?.IsNavigating ?? false;

    public bool IsReloadOrStopEnabled => (IsNavigating || CanReload) && !IsSettingsOpen;

    /// <summary>
    /// Returns true when the reload/stop button shows the reload icon. A navigation in flight shows the stop icon.
    /// </summary>
    public bool IsReloadIconVisible => !IsNavigating;

    public bool CanOpenInBrowser => HasPage;

    private string BlankTabLabel => _stringLocalizer.GetString("Browser_TabBar_NewTabLabel");

    // Code gen requires a parameterless constructor
    public BrowserDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public BrowserDocumentViewModel(
        ILogger<BrowserDocumentViewModel> logger,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IWindowModeService windowModeService,
        IMessengerService messengerService)
    {
        _logger = logger;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
        _messengerService = messengerService;

        _layoutMode = windowModeService.LayoutMode;
        messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        Bookmarks.CollectionChanged += Bookmarks_CollectionChanged;
    }

    /// <summary>
    /// Stops following application messages. Call when the document closes.
    /// </summary>
    public void StopObserving()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        LayoutMode = message.LayoutMode;
    }

    /// <summary>
    /// Reads the bookmarks from the document's file, and leaves the open tabs in place.
    /// </summary>
    public async Task<Result> LoadContent()
    {
        _isLoadingContent = true;

        try
        {
            return await LoadBookmarksAsync();
        }
        finally
        {
            _isLoadingContent = false;
        }
    }

    private async Task<Result> LoadBookmarksAsync()
    {
        // Reading through the gateway applies the same containment validation as writing.
        var resourceFileSystem = GetFileSystem();

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            PopulateBookmarks(Array.Empty<BrowserBookmark>());
            return Result.Ok();
        }

        var readResult = await LoadTextFromFileAsync();
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read '{ExplorerConstants.BrowserExtension}' file '{FileResource}'")
                .WithErrors(readResult);
        }

        var parseResult = BrowserFileContent.TryParse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return Result.Fail($"Failed to parse '{ExplorerConstants.BrowserExtension}' file '{FileResource}'")
                .WithErrors(parseResult);
        }
        var content = parseResult.Value;

        if (content.UnknownFields.Count > 0)
        {
            // The document still opens, and ToToml drops these keys on the next save.
            _logger.LogWarning(
                $"Browser document declares keys the application does not define ({string.Join(", ", content.UnknownFields)}): {FileResource}");
        }

        PopulateBookmarks(content.Bookmarks);

        return Result.Ok();
    }

    /// <summary>
    /// Scales the icon from a read of the tab's page to the size the document file stores, and shows the scaled icon
    /// on the tab. A bookmark at the tab's address stores the same scaled icon when the bookmark has no icon. A
    /// bookmark with an icon keeps that icon. When a site changes its icon, a bookmark therefore gets the new icon only
    /// after the user adds the bookmark again. The tab and the bookmarks keep their icons when the image is null or
    /// the encoder cannot read the image. They also keep their icons when the read is no longer the tab's current
    /// read.
    /// </summary>
    public void ReportTabFavicon(BrowserTabViewModel tab, int read, byte[]? image)
    {
        if (image is null
            || !tab.IsCurrentFaviconRead(read))
        {
            return;
        }

        // The encoder refuses an image wider or taller than MaximumSourcePixels. The encode is therefore quick enough
        // to run on the UI thread.
        var favicon = FaviconEncoder.Encode(image);
        if (favicon is null)
        {
            return;
        }

        tab.Favicon = favicon;

        foreach (var bookmark in Bookmarks)
        {
            if (bookmark.Favicon is null
                && BrowserUrlHelper.IsSameUrl(bookmark.Url, tab.CurrentUrl))
            {
                bookmark.Favicon = favicon;
            }
        }
    }

    // A tab at a bookmark's address uses the bookmark's icon until the page loads.
    private void ApplyBookmarkFavicon(BrowserTabViewModel tab)
    {
        foreach (var bookmark in Bookmarks)
        {
            if (bookmark.Favicon is not null
                && BrowserUrlHelper.IsSameUrl(bookmark.Url, tab.CurrentUrl))
            {
                tab.Favicon = bookmark.Favicon;
                return;
            }
        }
    }

    // The icon belongs to the bookmark's old address, so an address edit clears the icon. A tab at the new address then
    // gives the bookmark that tab's icon.
    private void ReplaceBookmarkFavicon(BrowserBookmarkViewModel bookmark)
    {
        bookmark.Favicon = null;

        CopyFaviconFromOpenTab(bookmark);
    }

    // Copies the icon of a tab at the bookmark's address. A tab only shows PNG icons that pass the file's check, so
    // this method copies the icon without scaling it.
    private void CopyFaviconFromOpenTab(BrowserBookmarkViewModel bookmark)
    {
        foreach (var tab in Tabs)
        {
            if (tab.Favicon is not null
                && BrowserUrlHelper.IsSameUrl(bookmark.Url, tab.CurrentUrl))
            {
                bookmark.Favicon = tab.Favicon;
                return;
            }
        }
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

        var content = new BrowserFileContent
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
    /// Validates a user-entered address, prefixing https:// when no scheme was typed. Returns false when the input
    /// cannot become a navigable external URL.
    /// </summary>
    public bool TryNormalizeUserUrl(string input, out string url)
    {
        return BrowserUrlHelper.TryNormalize(input, out url);
    }

    public void OpenInSystemBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        _commandService.Execute<IOpenSystemBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }

    /// <summary>
    /// Returns the document to the page.
    /// </summary>
    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    /// <summary>
    /// Opens a tab for each Open on Start bookmark and shows the first one. Does nothing when the document already has
    /// tabs.
    /// </summary>
    public void OpenStartTabs()
    {
        _isEditorStateReady = true;

        if (Tabs.Count > 0)
        {
            return;
        }

        foreach (var bookmark in Bookmarks)
        {
            if (bookmark.OpenOnStart
                && CreateBookmarkTab(bookmark) is { } tab)
            {
                Tabs.Add(tab);
            }
        }

        ShowTab(Tabs.FirstOrDefault());
    }

    /// <summary>
    /// Restores the settings and the open tabs from a saved editor state. A state that fails to parse, and a state with
    /// no tabs, open the Open on Start bookmarks.
    /// </summary>
    public void RestoreEditorState(string json)
    {
        var editorState = ParseEditorState(json);
        if (editorState is null)
        {
            OpenStartTabs();
            return;
        }

        SettingsSectionKey = editorState.SettingsSectionKey;
        IsSettingsOpen = editorState.SettingsOpen;

        var savedTabs = editorState.Tabs ?? Array.Empty<BrowserTabState>();
        if (savedTabs.Count == 0)
        {
            OpenStartTabs();
            return;
        }

        _isEditorStateReady = true;

        foreach (var savedTab in savedTabs)
        {
            var tab = BrowserTabViewModel.Create(BlankTabLabel, savedTab.Url, savedTab.Title);

            // A new bookmark can copy the tab's icon into the file, so the saved icon gets the file's check.
            if (savedTab.Favicon is { } favicon
                && BrowserBookmark.IsStorableFavicon(favicon))
            {
                tab.Favicon = favicon;
            }
            else
            {
                ApplyBookmarkFavicon(tab);
            }

            Tabs.Add(tab);
        }

        var shownIndex = Math.Clamp(editorState.ShownTabIndex, 0, Tabs.Count - 1);
        ShowTab(Tabs[shownIndex]);
    }

    private BrowserEditorState? ParseEditorState(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BrowserEditorState>(json, EditorStateSerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to restore the browser document editor state");
            return null;
        }
    }

    /// <summary>
    /// Returns the editor state as JSON, or null until opening the document has settled the tabs. A blank tab has
    /// nothing to restore, and a blob or data page is gone after a reload. The state therefore saves only the tabs with a
    /// web address. With another tab shown, the state shows the next saved tab to the right, or else the nearest one to
    /// the left.
    /// </summary>
    public string? TrySerializeEditorState()
    {
        if (!_isEditorStateReady)
        {
            return null;
        }

        var savedTabs = Tabs
            .Where(tab => tab.HasNavigablePage)
            .ToList();

        var shownIndex = 0;
        if (ShownTab is not null)
        {
            var shownPosition = Tabs.IndexOf(ShownTab);
            var savedShownTab = Tabs.Skip(shownPosition).FirstOrDefault(tab => tab.HasNavigablePage)
                ?? Tabs.Take(shownPosition).LastOrDefault(tab => tab.HasNavigablePage);

            if (savedShownTab is not null)
            {
                shownIndex = savedTabs.IndexOf(savedShownTab);
            }
        }

        var tabStates = savedTabs
            .Select(tab => new BrowserTabState(tab.CurrentUrl, tab.Title, tab.Favicon))
            .ToList();

        var editorState = new BrowserEditorState(IsSettingsOpen, SettingsSectionKey, tabStates, shownIndex);

        return JsonSerializer.Serialize(editorState, EditorStateSerializerOptions);
    }

    /// <summary>
    /// Shows a tab, or nothing for null.
    /// </summary>
    public void ShowTab(BrowserTabViewModel? tab)
    {
        ShownTab = tab;
    }

    /// <summary>
    /// Shows a tab the user or a page chose, and closes the settings because the page is behind them. The view gives
    /// the keyboard to the shown tab.
    /// </summary>
    public void ChooseTab(BrowserTabViewModel tab)
    {
        CloseSettings();
        ShowTab(tab);

        TabChosen?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a blank tab at the end of the tab bar, and shows it.
    /// </summary>
    public BrowserTabViewModel OpenBlankTab()
    {
        var tab = BrowserTabViewModel.Create(BlankTabLabel);
        Tabs.Add(tab);

        ChooseTab(tab);

        return tab;
    }

    /// <summary>
    /// Opens a bookmark in a new tab at the end of the tab bar, and shows the tab. Returns null for a bookmark that
    /// cannot be navigated to.
    /// </summary>
    public BrowserTabViewModel? OpenBookmark(BrowserBookmarkViewModel bookmark)
    {
        var tab = CreateBookmarkTab(bookmark);
        if (tab is null)
        {
            return null;
        }

        Tabs.Add(tab);

        ChooseTab(tab);

        return tab;
    }

    /// <summary>
    /// Sends the shown tab to an address the user chose, or opens a tab for the address with no tab shown. A
    /// bookmarked page shows the bookmark's icon until the page loads. Returns the tab, or null for an address that is
    /// not absolute. The view loads the page.
    /// </summary>
    public BrowserTabViewModel? NavigateShownTab(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination))
        {
            return null;
        }

        var tab = ShownTab ?? OpenBlankTab();
        tab.NotifyUserNavigation(destination.AbsoluteUri);
        ApplyBookmarkFavicon(tab);

        return tab;
    }

    // A tab for a bookmark's page, showing the bookmark's icon until the page loads. Null for a bookmark that cannot be
    // navigated to.
    private BrowserTabViewModel? CreateBookmarkTab(BrowserBookmarkViewModel bookmark)
    {
        if (!BrowserUrlHelper.TryGetAbsoluteUrl(bookmark.Url, out var url))
        {
            return null;
        }

        var tab = BrowserTabViewModel.Create(BlankTabLabel, url);
        tab.Favicon = bookmark.Favicon;

        return tab;
    }

    /// <summary>
    /// Opens a tab after the opener for a page's new window request, and shows it. Returns null for a page that asks
    /// by itself, with no user input behind the request. Also returns null for an address that cannot show in a tab. A
    /// mail address goes to the system's mail application. The page chooses the address, so any other scheme opens
    /// nothing.
    /// </summary>
    public BrowserTabViewModel? OpenTabForNewWindow(string url, bool isUserInitiated, BrowserTabViewModel? opener)
    {
        if (!isUserInitiated)
        {
            return null;
        }

        if (url.StartsWith(MailAddressPrefix, StringComparison.OrdinalIgnoreCase))
        {
            OpenInSystemBrowser(url);
            return null;
        }

        if (!CanShowInNewWindowTab(url))
        {
            return null;
        }

        var tab = BrowserTabViewModel.CreateForNewWindow(BlankTabLabel, url, opener);

        var openerIndex = -1;
        if (opener is not null)
        {
            openerIndex = Tabs.IndexOf(opener);
        }

        if (openerIndex >= 0)
        {
            Tabs.Insert(openerIndex + 1, tab);
        }
        else
        {
            Tabs.Add(tab);
        }

        ChooseTab(tab);

        return tab;
    }

    // An empty address names a window the page goes on to fill itself.
    private static bool CanShowInNewWindowTab(string url)
    {
        if (url.Length == 0)
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && NewWindowTabSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Closes a tab. Closing the shown tab shows the tab on the right, or the tab on the left when the closed tab was
    /// the last one.
    /// </summary>
    public void CloseTab(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasShown = ReferenceEquals(ShownTab, tab);

        RemoveTab(tab);

        if (!wasShown)
        {
            return;
        }

        if (Tabs.Count == 0)
        {
            ShowTab(null);
            return;
        }

        var nextIndex = Math.Min(index, Tabs.Count - 1);
        ShowTab(Tabs[nextIndex]);
    }

    /// <summary>
    /// Closes the tab of a page that closed itself, and shows its opener's tab again.
    /// </summary>
    public void ClosePageTab(BrowserTabViewModel tab)
    {
        var opener = tab.Opener;

        // Showing the opener first means the tab next to this one never counts as shown, so its page stays unloaded.
        if (ReferenceEquals(ShownTab, tab)
            && opener is not null
            && Tabs.Contains(opener))
        {
            ShowTab(opener);
        }

        CloseTab(tab);
    }

    /// <summary>
    /// Records a tab's navigation becoming a download. A new window's first navigation has no page to go back to, so
    /// its tab closes and the opener's tab shows again. Returns true when the tab closed.
    /// </summary>
    public bool HandleDownloadStarted(BrowserTabViewModel tab)
    {
        tab.NotifyDownloadStarted();

        if (!tab.IsNewWindow
            || tab.HasCommittedPage)
        {
            return false;
        }

        ClosePageTab(tab);

        return true;
    }

    /// <summary>
    /// Moves a tab to a new place on the tab bar.
    /// </summary>
    public void MoveTab(BrowserTabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(newIndex, 0, Tabs.Count - 1);
        if (targetIndex == oldIndex)
        {
            return;
        }

        Tabs.Move(oldIndex, targetIndex);
    }

    private void RemoveTab(BrowserTabViewModel tab)
    {
        Tabs.Remove(tab);

        tab.PropertyChanged -= ShownTab_PropertyChanged;

        foreach (var other in Tabs)
        {
            if (ReferenceEquals(other.Opener, tab))
            {
                other.Opener = null;
            }
        }
    }

    partial void OnShownTabChanged(BrowserTabViewModel? oldValue, BrowserTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= ShownTab_PropertyChanged;
            oldValue.IsShown = false;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += ShownTab_PropertyChanged;
            newValue.IsShown = true;
        }

        RaiseShownTabProperties();
    }

    private void ShownTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseShownTabProperties();
    }

    // The URL bar's properties all follow the shown tab, so they change together.
    private void RaiseShownTabProperties()
    {
        OnPropertyChanged(nameof(CurrentUrl));
        OnPropertyChanged(nameof(HasPage));
        OnPropertyChanged(nameof(HasNavigablePage));
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(IsPlaceholderVisible));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsLoadFailedVisible));
        OnPropertyChanged(nameof(AreEmptyPageBookmarksVisible));
        OnPropertyChanged(nameof(IsPageOnScreen));
        OnPropertyChanged(nameof(CanAddBookmarkFromCurrentPage));
        OnPropertyChanged(nameof(IsCurrentPageBookmarked));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(IsBackEnabled));
        OnPropertyChanged(nameof(IsForwardEnabled));
        OnPropertyChanged(nameof(IsNavigating));
        OnPropertyChanged(nameof(IsReloadOrStopEnabled));
        OnPropertyChanged(nameof(IsReloadIconVisible));
        OnPropertyChanged(nameof(CanOpenInBrowser));
    }

    /// <summary>
    /// Adds a bookmark for the shown page, named after its site.
    /// </summary>
    public BrowserBookmarkViewModel? AddBookmarkFromCurrentPage()
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

        return AddBookmark(new BrowserBookmark(pageUrl, name));
    }

    /// <summary>
    /// Adds a bookmark for the shown page, or removes the bookmark when one already points at the page.
    /// </summary>
    public void ToggleBookmarkForCurrentPage()
    {
        var bookmark = FindBookmarkForCurrentPage();
        if (bookmark is null)
        {
            AddBookmarkFromCurrentPage();
        }
        else
        {
            Bookmarks.Remove(bookmark);
        }
    }

    /// <summary>
    /// Adds a bookmark to the document. The first bookmark a document gains has Open on Start on, so the document has a
    /// page to open.
    /// </summary>
    public BrowserBookmarkViewModel AddBookmark(BrowserBookmark bookmark)
    {
        var bookmarkViewModel = CreateBookmark(bookmark);
        if (Bookmarks.Count == 0)
        {
            bookmarkViewModel.OpenOnStart = true;
        }

        Bookmarks.Add(bookmarkViewModel);

        if (bookmarkViewModel.Favicon is null)
        {
            CopyFaviconFromOpenTab(bookmarkViewModel);
        }

        return bookmarkViewModel;
    }

    /// <summary>
    /// Returns the bookmark pointing at the shown page, or null when no bookmark does.
    /// </summary>
    public BrowserBookmarkViewModel? FindBookmarkForCurrentPage()
    {
        foreach (var bookmark in Bookmarks)
        {
            if (BrowserUrlHelper.IsSameUrl(bookmark.Url, CurrentUrl))
            {
                return bookmark;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a bookmark for this document.
    /// </summary>
    public BrowserBookmarkViewModel CreateBookmark(BrowserBookmark bookmark)
    {
        var bookmarkViewModel = new BrowserBookmarkViewModel(_stringLocalizer)
        {
            Url = bookmark.Url,
            Name = bookmark.Name,
            OpenOnStart = bookmark.OpenOnStart,
            Favicon = bookmark.Favicon
        };

        return bookmarkViewModel;
    }

    protected override IResourceFileSystem GetFileSystem()
    {
        // Routing the base-class load and save helpers through the injected wrapper lets tests substitute the file
        // system without a service locator.
        return _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    }

    // Records an edit against the document, unless the change came from the load.
    private void RecordDataChanged()
    {
        if (_isLoadingContent)
        {
            return;
        }

        OnDataChanged();
    }

    // Replaces the bookmarks with those just read off disk. The collection handler keeps each bookmark's edits wired
    // up, so the load goes through the collection.
    private void PopulateBookmarks(IReadOnlyList<BrowserBookmark> bookmarks)
    {
        // Clearing raises a reset, and a reset does not report the old items. This method therefore detaches the
        // outgoing bookmarks before clearing them.
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
            foreach (BrowserBookmarkViewModel bookmark in e.OldItems)
            {
                bookmark.PropertyChanged -= Bookmark_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BrowserBookmarkViewModel bookmark in e.NewItems)
            {
                bookmark.PropertyChanged += Bookmark_PropertyChanged;
            }
        }

        RaiseBookmarkListProperties();

        RecordDataChanged();
    }

    // The bookmark list, the empty page's list and the star button all follow the bookmarks' addresses.
    private void RaiseBookmarkListProperties()
    {
        OnPropertyChanged(nameof(NavigableBookmarks));
        OnPropertyChanged(nameof(EmptyPageBookmarks));
        OnPropertyChanged(nameof(AreEmptyPageBookmarksVisible));
        OnPropertyChanged(nameof(CanAddBookmarkFromCurrentPage));
        OnPropertyChanged(nameof(IsCurrentPageBookmarked));
    }

    private void Bookmark_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The stored properties only. A bookmark also reports the display properties derived from these.
        if (e.PropertyName != nameof(BrowserBookmarkViewModel.Url)
            && e.PropertyName != nameof(BrowserBookmarkViewModel.Name)
            && e.PropertyName != nameof(BrowserBookmarkViewModel.OpenOnStart)
            && e.PropertyName != nameof(BrowserBookmarkViewModel.Favicon))
        {
            return;
        }

        if (e.PropertyName == nameof(BrowserBookmarkViewModel.Url))
        {
            RaiseBookmarkListProperties();

            if (sender is BrowserBookmarkViewModel bookmark)
            {
                ReplaceBookmarkFavicon(bookmark);
            }
        }

        RecordDataChanged();
    }

    // The host a page is on, plus the port when the port differs from the scheme's default. A leading "www." is dropped
    // when a dot would still remain.
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

    // A web view reports an internationalized domain in its ASCII form. This turns it back into the name the user
    // reads.
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
}
