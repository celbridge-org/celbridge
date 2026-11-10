using Celbridge.Browser.ViewModels;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Services;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Automation;
using System.Diagnostics;
using Windows.System;

namespace Celbridge.Browser.Views;

/// <summary>
/// Shows the external pages of a .browser document in tabs. A URL bar and a tab bar sit above the page, and the settings
/// surface takes the page's place.
/// </summary>
public sealed partial class BrowserDocumentView : DocumentView, IBrowserFindTarget
{
    /// <summary>
    /// A tab's content: a panel in the document area, and the web view in that panel. The web view arrives the first time
    /// the tab is on screen.
    /// </summary>
    private sealed class BrowserTabContent
    {
        public BrowserTabContent(BrowserTabViewModel tab, Grid panel)
        {
            Tab = tab;
            Panel = panel;
        }

        public BrowserTabViewModel Tab { get; }

        public Grid Panel { get; }

        public IEditorWebView? WebView { get; set; }

        public bool IsAcquiringWebView { get; set; }
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BrowserDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;

    private readonly List<BrowserTabContent> _tabContents = new();

    // Windows abandons a download when its web view closes. The web view of a tab that closed for a download therefore
    // stays hidden until the document closes.
    private readonly List<IEditorWebView> _webViewsKeptForDownloads = new();

    // The web views' accessible name, stored until each web view is acquired.
    private string _accessibleName = string.Empty;

    // Set when the document closes. A web view that arrives after that is disposed straight away.
    private bool _isClosed;

    // Set when the shown tab's page should take the keyboard as soon as its web view arrives.
    private bool _focusPageOnArrival;

    // The web view running the find session. The session ends on that page after a tab change.
    private IEditorWebView? _findWebView;

    public BrowserDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    private string BookmarksTooltipString => _stringLocalizer.GetString("Browser_UrlBar_BookmarksTooltip");
    private string BackTooltipString => _stringLocalizer.GetString("Browser_UrlBar_BackTooltip");
    private string ForwardTooltipString => _stringLocalizer.GetString("Browser_UrlBar_ForwardTooltip");
    private string AddressPlaceholderString => _stringLocalizer.GetString("Browser_UrlBar_AddressPlaceholder");
    private string OpenInBrowserTooltipString => _stringLocalizer.GetString("Browser_UrlBar_OpenInBrowserTooltip");
    private string SettingsTooltipString => _stringLocalizer.GetString("Browser_UrlBar_SettingsTooltip");
    private string PlaceholderHintString => _stringLocalizer.GetString("Browser_Placeholder_Hint");
    private string PlaceholderLoadFailedString => _stringLocalizer.GetString("Browser_Placeholder_LoadFailed");
    private string PlaceholderLoadFailedHintString => _stringLocalizer.GetString("Browser_Placeholder_LoadFailedHint");
    private string EditBookmarksString => _stringLocalizer.GetString("Browser_UrlBar_EditBookmarks");

    public BrowserDocumentView(
        IServiceProvider serviceProvider,
        ILogger<BrowserDocumentView> logger,
        ICommandService commandService,
        IStringLocalizer stringLocalizer,
        IWebViewFactory webViewFactory,
        IWebViewService webViewService)
    {
        // The localizer and view model back x:Bind paths, so both must exist
        // before InitializeComponent evaluates the bindings.
        _serviceProvider = serviceProvider;
        _logger = logger;
        _commandService = commandService;
        _stringLocalizer = stringLocalizer;
        _webViewFactory = webViewFactory;
        _webViewService = webViewService;
        _webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();

        ViewModel = serviceProvider.GetRequiredService<BrowserDocumentViewModel>();

        this.InitializeComponent();

        TabBar.ViewModel = ViewModel;
        TabBar.TabCloseRequested += TabBar_TabCloseRequested;
        TabBar.SetDragOverlay(TabDragOverlay);

        var overlayFlyoutSupport = ServiceLocator.AcquireService<IOverlayFlyoutSupport>();
        overlayFlyoutSupport.Apply(BookmarkListFlyout, FocusDocumentContent);

        FindBar.Attach(this);
        FindBar.Closed += OnFindBarClosed;

        SettingsSurface.ReturnToPageRequested += SettingsSurface_ReturnToPageRequested;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.TabChosen += ViewModel_TabChosen;
        UpdateReloadOrStopTooltip();
        UpdateBookmarkPageButton();
        UpdatePlaceholderName();

        Loaded += BrowserDocumentView_Loaded;
    }

    // The tab view unloads a document tab it is not showing, so Loaded marks the document coming on screen.
    private void BrowserDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyShownTab();
    }

    private BrowserTabContent? FindContent(BrowserTabViewModel? tab)
    {
        return _tabContents.FirstOrDefault(content => ReferenceEquals(content.Tab, tab));
    }

    private BrowserTabContent? FindContent(object? webView)
    {
        return _tabContents.FirstOrDefault(content => ReferenceEquals(content.WebView, webView));
    }

    private IEditorWebView? ShownWebView => FindContent(ViewModel.ShownTab)?.WebView;

    private BrowserTabContent GetOrCreateContent(BrowserTabViewModel tab)
    {
        var content = FindContent(tab);
        if (content is not null)
        {
            return content;
        }

        var panel = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        AppWebViewContainer.Children.Add(panel);

        content = new BrowserTabContent(tab, panel);
        _tabContents.Add(content);

        return content;
    }

    // Puts the shown tab's panel on screen. The other panels stay attached, because the Skia heads do not raise
    // navigation events for a page navigated while detached. A hidden page keeps running. A web view that keeps the
    // keyboard while hidden takes every keystroke on macOS, so the keyboard moves to what now shows.
    private void ApplyShownTab()
    {
        var shownTab = ViewModel.ShownTab;

        var isKeyboardInHiddenPage = false;
        foreach (var content in _tabContents)
        {
            var isShown = ReferenceEquals(content.Tab, shownTab);
            content.Panel.Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;

            if (!isShown
                && content.WebView is not null
                && _webViewFocusRegistry.IsFocusedSurface(content.WebView))
            {
                isKeyboardInHiddenPage = true;
            }
        }

        // Find applies to the page it started on.
        if (FindBar.Visibility == Visibility.Visible
            && !ReferenceEquals(_findWebView, ShownWebView))
        {
            FindBar.Close();
        }

        if (shownTab is not null)
        {
            var shownContent = GetOrCreateContent(shownTab);
            shownContent.Panel.Visibility = Visibility.Visible;

            // A document in a background document tab loads nothing until the user shows that document tab. A new
            // window's web view comes from the page's request.
            if (IsLoaded
                && !shownTab.IsNewWindow)
            {
                _ = AcquireWebViewAsync(shownContent);
            }

            TabBar.BringShownTabIntoView();
        }

        ApplyContentLayout();

        if (isKeyboardInHiddenPage)
        {
            FocusDocumentContent();
        }
    }

    private async Task AcquireWebViewAsync(BrowserTabContent content)
    {
        if (content.WebView is not null
            || content.IsAcquiringWebView)
        {
            return;
        }

        content.IsAcquiringWebView = true;

        // Catches every failure, so the tab stays empty and the error is logged.
        try
        {
            var acquireTimer = Stopwatch.StartNew();

            var webView = await _webViewFactory.AcquireAsync(CreateWebViewOptions());

            _logger.LogDebug(
                "A web view arrived after {ElapsedMs} ms for the browser tab at '{Url}' in {Resource}",
                acquireTimer.ElapsedMilliseconds,
                content.Tab.CurrentUrl,
                FileResource);

            if (_isClosed
                || !_tabContents.Contains(content))
            {
                webView.Dispose();
                return;
            }

            SetUpWebView(content, webView);

            if (content.Tab.HasPage)
            {
                LoadAddress(webView, content.Tab.CurrentUrl);
            }

            GrantPendingFocus(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the web view of a browser tab");
        }
        finally
        {
            content.IsAcquiringWebView = false;
        }
    }

    private void SetUpWebView(BrowserTabContent content, IEditorWebView webView)
    {
        content.WebView = webView;

        webView.SetResource(FileResource);
        webView.SetAccessibleName(_accessibleName);

        webView.NavigationStarting += WebView_NavigationStarting;
        webView.NavigationCommitted += WebView_NavigationCommitted;
        webView.NavigationCompleted += WebView_NavigationCompleted;
        webView.NewWindowRequested += WebView_NewWindowRequested;
        webView.PageClosed += WebView_PageClosed;
        webView.DocumentTitleChanged += WebView_DocumentTitleChanged;
        webView.FaviconChanged += WebView_FaviconChanged;
        webView.HistoryChanged += WebView_HistoryChanged;
        webView.DownloadStarted += WebView_DownloadStarted;
        webView.LoadedEmpty += WebView_LoadedEmpty;

        webView.AttachTo(content.Panel);

        // The application does not run its own script in the page. Releasing focus therefore does nothing here. The
        // registry's native click monitor tracks focus on the page.
        RegisterWebSurfaceFocus(webView, releaseFocus: () => { });

        // No host RPC channel is opened. The page is untrusted third-party content, and the native message bus is
        // unauthenticated, so a channel would let the page drive host RPC methods.
    }

    private void GrantPendingFocus(BrowserTabContent content)
    {
        if (!_focusPageOnArrival
            || content.WebView is null
            || !ReferenceEquals(content.Tab, ViewModel.ShownTab))
        {
            return;
        }

        _focusPageOnArrival = false;
        _webViewFocusRegistry.GrantFocus(content.WebView);
    }

    private WebViewOptions CreateWebViewOptions()
    {
        // Some sites flag the default macOS WKWebView UA as an unsupported browser. On macOS, a User-Agent token
        // replaces that UA.
        var environmentInfo = _serviceProvider.GetRequiredService<IAppEnvironment>().GetEnvironmentInfo();

        return new WebViewOptions
        {
            // A page that does not set a background sits on white, the default in a browser. On a transparent view, the
            // page's default black text would sit on the dark theme.
            BackgroundColor = Colors.White,
            AreDevToolsEnabled = _webViewService.IsDevToolsFeatureEnabled(),

            // The page is browsed content, so user zoom stays enabled.
            IsZoomEnabled = true,
            UserAgentToken = $"Celbridge/{environmentInfo.AppVersion}",
        };
    }

    // Opens an address the user chose in the shown tab, or in a new tab with none shown. The address bar names the
    // address at once. A web view still on the way loads the tab's address on arrival.
    private void SendShownTabTo(string url)
    {
        var tab = ViewModel.NavigateShownTab(url);
        if (tab is null)
        {
            _logger.LogWarning($"Cannot navigate to invalid URL: '{url}'");
            return;
        }

        var webView = FindContent(tab)?.WebView;

        _logger.LogDebug(
            "The user sent a browser tab to '{Url}', and its web view is {WebViewState}",
            tab.CurrentUrl,
            webView is null ? "still on the way" : "ready");

        webView?.Navigate(tab.CurrentUrl);
    }

    private void LoadAddress(IEditorWebView webView, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination))
        {
            _logger.LogWarning($"Cannot navigate to invalid URL: '{url}'");
            return;
        }

        _logger.LogDebug("A browser tab's web view is loading '{Url}'", destination.AbsoluteUri);

        webView.Navigate(destination.AbsoluteUri);
    }

    private void WebView_HistoryChanged(object? sender, EventArgs e)
    {
        var content = FindContent(sender);
        if (content?.WebView is null)
        {
            return;
        }

        UpdateNavigationState(content);
    }

    private void WebView_NavigationCompleted(object? sender, WebNavigationCompletedEventArgs e)
    {
        var content = FindContent(sender);
        if (content is null)
        {
            return;
        }

        _logger.LogDebug("A browser tab's navigation ended with {Result} at '{Url}'", e.Result, content.Tab.CurrentUrl);

        content.Tab.NotifyNavigationCompleted(ResolveNavigationOutcome(e));
        UpdateNavigationState(content);
    }

    // Only a failed navigation means the page failed to load. A canceled or abandoned navigation leaves the old page on
    // screen. Such a navigation may be one a later navigation superseded, one the user stopped, or one that became a
    // download.
    private static NavigationOutcome ResolveNavigationOutcome(WebNavigationCompletedEventArgs e)
    {
        return e.Result switch
        {
            WebNavigationResult.Succeeded => NavigationOutcome.Loaded,
            WebNavigationResult.Failed => NavigationOutcome.Failed,
            _ => NavigationOutcome.Aborted
        };
    }

    // An empty page is reported as a failure, so the document shows the load-failed placeholder and its reload. A blank
    // page would look the same as a slow one to the user.
    private void WebView_LoadedEmpty(object? sender, EventArgs e)
    {
        FindContent(sender)?.Tab.NotifyLoadedEmpty();
    }

    private void WebView_NavigationStarting(object? sender, WebNavigationStartingEventArgs args)
    {
        _logger.LogDebug("A browser tab's navigation started for '{Url}'", args.Uri);

        FindContent(sender)?.Tab.NotifyNavigationStarted(args.Uri);
    }

    // The address bar shows a page's address once the navigation commits.
    private void WebView_NavigationCommitted(object? sender, string url)
    {
        _logger.LogDebug("A browser tab's navigation committed to '{Url}'", url);

        FindContent(sender)?.Tab.NotifyNavigationCommitted(url);
    }

    private void WebView_DocumentTitleChanged(object? sender, EventArgs e)
    {
        var content = FindContent(sender);
        if (content?.WebView is null)
        {
            return;
        }

        content.Tab.Title = content.WebView.DocumentTitle;
    }

    // An exception here must not escape, because the platform raises this from a native callback.
    private async void WebView_FaviconChanged(object? sender, EventArgs e)
    {
        var content = FindContent(sender);
        if (content?.WebView is null)
        {
            return;
        }

        try
        {
            var read = content.Tab.BeginFaviconRead();
            var image = await content.WebView.ReadFaviconAsync();
            ViewModel.ReportTabFavicon(content.Tab, read, image);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read a page's favicon");
        }
    }

    // A response that turns out to be an attachment abandons the navigation for the download, and the tab's page stays
    // on screen. The tab of a new window with no page closes.
    private void WebView_DownloadStarted(object? sender, EventArgs e)
    {
        var content = FindContent(sender);
        if (content is null)
        {
            return;
        }

        if (!ViewModel.HandleDownloadStarted(content.Tab))
        {
            return;
        }

        _tabContents.Remove(content);
        content.Panel.Visibility = Visibility.Collapsed;
        if (content.WebView is not null)
        {
            _webViewsKeptForDownloads.Add(content.WebView);
        }

        FocusDocumentContent();
    }

    // The page asks while the platform waits on the answer, so this answers on every path.
    private async void WebView_NewWindowRequested(object? sender, WebNewWindowRequest request)
    {
        var opener = FindContent(sender)?.Tab;

        var tab = ViewModel.OpenTabForNewWindow(request.Url, request.IsUserInitiated, opener);
        if (tab is null)
        {
            request.Decline();
            return;
        }

        BrowserTabContent? content = null;
        try
        {
            content = GetOrCreateContent(tab);
            content.IsAcquiringWebView = true;

            var webView = await _webViewFactory.AcquireForNewWindowAsync(request, CreateWebViewOptions());
            if (_isClosed
                || !_tabContents.Contains(content))
            {
                request.Decline();
                webView.Dispose();
                return;
            }

            SetUpWebView(content, webView);
            await request.AcceptAsync(webView);

            GrantPendingFocus(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open a page's new window in a tab");
            request.Decline();

            if (content is not null)
            {
                CloseTabAndContent(tab);
            }
        }
        finally
        {
            if (content is not null)
            {
                content.IsAcquiringWebView = false;
            }
        }
    }

    // The platform raises this from inside its own callback, so the web view closes on the next dispatcher turn.
    private void WebView_PageClosed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var content = FindContent(sender);
            if (content is null)
            {
                return;
            }

            ViewModel.ClosePageTab(content.Tab);
            ReleaseContent(content);
            FocusDocumentContent();
        });
    }

    private void TabBar_TabCloseRequested(object? sender, BrowserTabViewModel tab)
    {
        CloseTabAndContent(tab);
        FocusDocumentContent();
    }

    private void CloseTabAndContent(BrowserTabViewModel tab)
    {
        ViewModel.CloseTab(tab);

        var content = FindContent(tab);
        if (content is not null)
        {
            ReleaseContent(content);
        }
    }

    private void ReleaseContent(BrowserTabContent content)
    {
        _tabContents.Remove(content);

        content.WebView?.Dispose();
        content.WebView = null;

        AppWebViewContainer.Children.Remove(content.Panel);
    }

    private void ViewModel_TabChosen(object? sender, EventArgs e)
    {
        FocusDocumentContent();
    }

    // Built on each opening, so the list always matches the bookmarks in the settings.
    private void BookmarkListFlyout_Opening(object? sender, object e)
    {
        BookmarkListFlyout.Items.Clear();

        foreach (var bookmark in ViewModel.NavigableBookmarks)
        {
            var item = new MenuFlyoutItem
            {
                Text = bookmark.DisplayName,
                Tag = bookmark,
                Icon = FaviconImage.CreateMenuIcon(bookmark.Favicon)
            };
            ToolTipService.SetToolTip(item, bookmark.Url);
            item.Click += BookmarkItem_Click;

            BookmarkListFlyout.Items.Add(item);
        }

        if (BookmarkListFlyout.Items.Count > 0)
        {
            BookmarkListFlyout.Items.Add(new MenuFlyoutSeparator());
        }

        var editItem = new MenuFlyoutItem
        {
            Text = EditBookmarksString
        };
        editItem.Click += EditBookmarksItem_Click;

        BookmarkListFlyout.Items.Add(editItem);
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        var item = (FrameworkElement)sender;
        if (item.Tag is BrowserBookmarkViewModel bookmark)
        {
            ViewModel.OpenBookmark(bookmark);
        }
    }

    private void EditBookmarksItem_Click(object sender, RoutedEventArgs e)
    {
        ShowBookmarksSection();
    }

    // A bookmark on the empty page opens in the shown tab, or in a new tab with no tab shown. A web view still on the way
    // loads the tab's address on arrival.
    private void EmptyPageBookmark_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is not BrowserBookmarkViewModel bookmark
            || !ViewModel.TryNormalizeUserUrl(bookmark.Url, out var url))
        {
            return;
        }

        SendShownTabTo(url);

        GiveFocusToWebContent();
    }

    // The link hides with the placeholder, so the settings take the keyboard.
    private void EditBookmarksLink_Click(object sender, RoutedEventArgs e)
    {
        ShowBookmarksSection();

        FocusDocumentContent();
    }

    private void UpdateNavigationState(BrowserTabContent content)
    {
        if (content.WebView is null)
        {
            return;
        }

        content.Tab.CanGoBack = content.WebView.CanGoBack;
        content.Tab.CanGoForward = content.WebView.CanGoForward;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var webView = ShownWebView;
        if (webView is not null
            && webView.CanGoBack)
        {
            webView.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var webView = ShownWebView;
        if (webView is not null
            && webView.CanGoForward)
        {
            webView.GoForward();
        }
    }

    private async void ReloadOrStopButton_Click(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.ShownTab;
        var webView = ShownWebView;
        if (tab is null
            || webView is null)
        {
            return;
        }

        try
        {
            if (tab.IsNavigating)
            {
                tab.NotifyNavigationStopped();
                await webView.StopAsync();
            }
            else
            {
                // Reload acts on the page, so an uncommitted edit in the address box is dropped.
                SyncAddressText();
                await webView.ReloadAsync(clearCache: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload or stop the page");
        }
    }

    // Puts the address the page is showing back in the box, discarding an edit the user typed but never committed.
    private void SyncAddressText()
    {
        AddressTextBox.Text = ViewModel.AddressText;
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenInSystemBrowser(ViewModel.CurrentUrl);
    }

    private void BookmarkPageButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleBookmarkForCurrentPage();
    }

    private void ShowBookmarksSection()
    {
        // Opening the settings builds them on the stored section, so the key is set first. A surface already built
        // ignores that key, and is sent to the section directly below.
        ViewModel.SettingsSectionKey = BrowserDocumentSettingsView.BookmarksSectionKey;
        ViewModel.IsSettingsOpen = true;

        SettingsSurface.SelectSection(BrowserDocumentSettingsView.BookmarksSectionKey);
    }

    private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Tab moves focus here, and an address has no tab or line break anyway.
        SingleLineText.RemoveTabsAndLineBreaks(AddressTextBox);
    }

    private void AddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // Committing an address is a request to see a page, so the settings give the document area back. An
            // address that cannot be navigated to leaves them where they are.
            var address = AddressTextBox.Text.Trim();
            if (address.Length == 0)
            {
                // Committing an empty address blanks the shown tab, and the placeholder shows.
                ViewModel.CloseSettings();
                if (ViewModel.ShownTab is not null)
                {
                    SendShownTabTo("about:blank");
                }
            }
            else if (ViewModel.TryNormalizeUserUrl(address, out var url))
            {
                ViewModel.CloseSettings();

                // Navigating to the address already showing does not raise a change for the binding to follow. An
                // edit in the box would then survive the navigation.
                SyncAddressText();
                SendShownTabTo(url);

                // The next keystroke reaches the content the user just navigated to, and the panel focus reflects it.
                GiveFocusToWebContent();
            }

            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            var isEditing = AddressTextBox.Text != ViewModel.AddressText;

            // Abandon the edit, restoring the address the page is showing.
            SyncAddressText();

            // With no edit to abandon, Escape leaves the settings.
            if (ViewModel.IsSettingsOpen
                && !isEditing)
            {
                ReturnToPage();
            }
            else if (ViewModel.IsPageOnScreen)
            {
                GiveFocusToWebContent();
            }

            e.Handled = true;
        }
    }

    private void SettingsSurface_ReturnToPageRequested(object? sender, EventArgs e)
    {
        ReturnToPage();
    }

    // Escape leaves the settings from anywhere in the document, unless a control inside handled it first.
    private void LayoutRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape
            || !ViewModel.IsSettingsOpen)
        {
            return;
        }

        ReturnToPage();

        e.Handled = true;
    }

    private void ReturnToPage()
    {
        ViewModel.CloseSettings();

        FocusDocumentContent();
    }

    // Gives the document area to whichever of the page, the settings and the placeholder belongs there. The web views
    // are collapsed while either of the other two shows. A hosted web view is a native view above the canvas those two
    // are drawn on. While shown, a web view takes mouse input meant for them, and the cursor answers to the page.
    private void ApplyContentLayout()
    {
        var showSettings = ViewModel.IsSettingsOpen;
        if (showSettings)
        {
            SettingsSurface.Initialize(ViewModel, ViewModel.SettingsSectionKey);

            // Find applies to the page, and the settings hide the page.
            if (FindBar.Visibility == Visibility.Visible)
            {
                FindBar.Close();
            }
        }

        SettingsSurface.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        ContentPlaceholder.Visibility = ViewModel.IsPlaceholderVisible ? Visibility.Visible : Visibility.Collapsed;
        AppWebViewContainer.Visibility = ViewModel.IsPageOnScreen ? Visibility.Visible : Visibility.Collapsed;

        UpdatePlaceholderName();

        // A hidden page passes the keyboard on, since on macOS a hidden web view holding the keyboard keeps every
        // keystroke.
        var webView = ShownWebView;
        if (!ViewModel.IsPageOnScreen
            && webView is not null
            && _webViewFocusRegistry.IsFocusedSurface(webView))
        {
            FocusDocumentContent();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserDocumentViewModel.IsNavigating))
        {
            UpdateReloadOrStopTooltip();
        }
        else if (e.PropertyName == nameof(BrowserDocumentViewModel.IsCurrentPageBookmarked))
        {
            UpdateBookmarkPageButton();
        }
        else if (e.PropertyName == nameof(BrowserDocumentViewModel.ShownTab))
        {
            ApplyShownTab();
        }
        else if (e.PropertyName == nameof(BrowserDocumentViewModel.AreToolbarsVisible))
        {
            ReleaseKeyboardFromHiddenToolbars();
        }
        else if (e.PropertyName == nameof(BrowserDocumentViewModel.IsSettingsOpen)
            || e.PropertyName == nameof(BrowserDocumentViewModel.IsPageOnScreen))
        {
            ApplyContentLayout();
        }
    }

    // This handler runs before the bindings collapse the toolbars, so the focused element is still inside them.
    private void ReleaseKeyboardFromHiddenToolbars()
    {
        if (ViewModel.AreToolbarsVisible
            || !IsKeyboardInToolbars())
        {
            return;
        }

        FocusDocumentContent();
    }

    private bool IsKeyboardInToolbars()
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var element = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (ReferenceEquals(element, UrlBar)
                || ReferenceEquals(element, TabBar))
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void UpdateReloadOrStopTooltip()
    {
        var key = ViewModel.IsNavigating ? "Browser_UrlBar_StopTooltip" : "Browser_UrlBar_ReloadTooltip";
        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(ReloadOrStopButton, tooltip);
        AutomationProperties.SetName(ReloadOrStopButton, tooltip);
    }

    private void UpdateBookmarkPageButton()
    {
        var isBookmarked = ViewModel.IsCurrentPageBookmarked;

        BookmarkPageIcon.Symbol = isBookmarked ? IconSymbol.StarFilled : IconSymbol.Star;

        var key = isBookmarked ? "Browser_UrlBar_RemoveBookmarkTooltip" : "Browser_UrlBar_BookmarkPageTooltip";
        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(BookmarkPageButton, tooltip);
        AutomationProperties.SetName(BookmarkPageButton, tooltip);
    }

    // The placeholder takes the keyboard when nothing else in the document can, so the accessible name repeats the text.
    private void UpdatePlaceholderName()
    {
        var name = ViewModel.IsLoadFailedVisible ? PlaceholderLoadFailedString : PlaceholderHintString;

        AutomationProperties.SetName(PlaceholderContent, name);
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var setResult = await base.SetFileResource(fileResource);
        if (setResult.IsFailure)
        {
            return setResult;
        }

        // A rename reuses this view, so every tab's web view follows the resource.
        foreach (var content in _tabContents)
        {
            content.WebView?.SetResource(FileResource);
        }

        foreach (var webView in _webViewsKeptForDownloads)
        {
            webView.SetResource(FileResource);
        }

        return setResult;
    }

    // Reads the bookmarks from the file.
    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        return await ViewModel.SaveDocumentContent();
    }

    public override async Task<string?> TrySaveEditorStateAsync()
    {
        await Task.CompletedTask;

        // The surface builds the sections on first use. A document that never opened the settings keeps the restored
        // section key.
        var sectionKey = SettingsSurface.SelectedSectionKey;
        if (!string.IsNullOrEmpty(sectionKey))
        {
            ViewModel.SettingsSectionKey = sectionKey;
        }

        return ViewModel.TrySerializeEditorState();
    }

    // WakeFailures counts wakes missed in a row, so the document reports the worst tab. ProcessFailures counts deaths,
    // so the document reports the total.
    public override WebViewHealth GetHealth()
    {
        var wakeFailures = 0;
        var processFailures = 0;

        foreach (var content in _tabContents)
        {
            var health = content.WebView?.GetHealth();
            if (health is null)
            {
                continue;
            }

            wakeFailures = Math.Max(wakeFailures, health.WakeFailures);
            processFailures += health.ProcessFailures;
        }

        return new WebViewHealth(wakeFailures, processFailures);
    }

    // A document with no saved state opens its Open on Start bookmarks.
    public override async Task CompleteOpenAsync(string? editorState)
    {
        await Task.CompletedTask;

        if (editorState is null)
        {
            ViewModel.OpenStartTabs();
        }
        else
        {
            ViewModel.RestoreEditorState(editorState);
        }

        // The state may leave the view model's values unchanged. The view model then does not raise a property change.
        ApplyShownTab();
    }

    // Closing the document discards the open tabs, so the next open shows the Open on Start bookmarks.
    public override bool DiscardsEditorStateOnClose => true;

    public override IEditTarget EditTarget { get; } = new PlatformEditTarget();

    public override void SetAccessibleName(string name)
    {
        _accessibleName = name;

        foreach (var content in _tabContents)
        {
            content.WebView?.SetAccessibleName(name);
        }
    }

    public override void FocusDocument()
    {
        FocusDocumentContent();
    }

    // Gives the keyboard to whatever fills the document area: the settings rail, the page, the URL bar, or the
    // placeholder when the toolbars are hidden.
    private void FocusDocumentContent()
    {
        if (ViewModel.IsSettingsOpen)
        {
            _focusPageOnArrival = false;

            if (!SettingsSurface.FocusRail())
            {
                _logger.LogDebug("The browser settings rail did not take focus");
            }

            return;
        }

        if (ViewModel.IsPageOnScreen)
        {
            GiveFocusToWebContent();
            return;
        }

        _focusPageOnArrival = false;

        if (ViewModel.AreToolbarsVisible)
        {
            AddressTextBox.Focus(FocusState.Programmatic);
            return;
        }

        // The placeholder takes the keyboard even with nothing to act on, which moves the keyboard off the last control.
        PlaceholderContent.Focus(FocusState.Programmatic);
    }

    // Call this only for a page that is on screen, or one a navigation has just started to show. On macOS, native focus
    // lands on a hidden web view all the same, and that view takes every keystroke. A web view still on its way takes
    // the keyboard on arrival.
    private void GiveFocusToWebContent()
    {
        var webView = ShownWebView;
        if (webView is null)
        {
            _focusPageOnArrival = true;
            return;
        }

        _focusPageOnArrival = false;
        _webViewFocusRegistry.GrantFocus(webView);
    }

    // True when the find bar can drive this document. The page must be on screen and the web view live, and the backend
    // must lack a find UI. The Windows Chromium heads have one, so they report false and keep their built-in bar.
    public override bool CanFind
    {
        get
        {
            var webView = ShownWebView;

            return ViewModel.IsPageOnScreen
                && webView is not null
                && !webView.ProvidesBuiltInFind;
        }
    }

    public override bool TryBeginFind()
    {
        if (!CanFind)
        {
            return false;
        }

        _findWebView = ShownWebView;
        FindBar.Begin();
        return true;
    }

    private void OnFindBarClosed(object? sender, EventArgs e)
    {
        // The settings close the bar as they open, and the keyboard stays with the control that opened them.
        if (ViewModel.IsSettingsOpen)
        {
            return;
        }

        FocusDocumentContent();
    }

    async Task IBrowserFindTarget.StartFindAsync(string term, FindOptions options)
    {
        if (_findWebView is not null)
        {
            await _findWebView.StartFindAsync(term, options);
        }
    }

    void IBrowserFindTarget.FindNext()
    {
        _findWebView?.FindNext();
    }

    void IBrowserFindTarget.FindPrevious()
    {
        _findWebView?.FindPrevious();
    }

    void IBrowserFindTarget.StopFind()
    {
        _findWebView?.StopFind();
        _findWebView = null;
    }

    public override async Task PrepareToClose()
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.TabChosen -= ViewModel_TabChosen;
        ViewModel.StopObserving();

        _isClosed = true;

        foreach (var content in _tabContents)
        {
            content.WebView?.Dispose();
            content.WebView = null;
        }

        _tabContents.Clear();

        foreach (var webView in _webViewsKeptForDownloads)
        {
            webView.Dispose();
        }

        _webViewsKeptForDownloads.Clear();

        await base.PrepareToClose();
    }
}
