using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

namespace Celbridge.WebView.Views;

/// <summary>
/// The per-user view state of a .webview document: whether the settings are open and which section they
/// are showing. Persisted through the document editor state, not the .webview file.
/// </summary>
internal sealed record WebViewEditorState(bool SettingsOpen, string SettingsSectionKey);

/// <summary>
/// Hosts the external page a .webview document opens, with a browser-style URL bar above the page and a
/// resizable settings panel over it.
/// </summary>
public sealed partial class WebViewDocumentView : DocumentView, IWebViewFindTarget, IDocumentChromeOwner
{
    private static readonly JsonSerializerOptions EditorStateSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebViewDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;

    private IEditorWebView? _webView;

    // The web view's accessible name, stored until the web view is acquired.
    private string _accessibleName = string.Empty;

    // Set on the first initialization attempt, so LoadContent and Loaded share a single run.
    private Task? _initializeWebViewTask;

    // Set when the document closes. A web view that arrives after that is disposed straight away.
    private bool _isClosed;

    // The section the settings reopen on, carried until the surface is built on first use.
    private string _settingsSectionKey = string.Empty;

    public WebViewDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    private string BackTooltipString => _stringLocalizer.GetString("WebView_UrlBar_BackTooltip");
    private string ForwardTooltipString => _stringLocalizer.GetString("WebView_UrlBar_ForwardTooltip");
    private string HomeTooltipString => _stringLocalizer.GetString("WebView_UrlBar_HomeTooltip");
    private string AddressPlaceholderString => _stringLocalizer.GetString("WebView_UrlBar_AddressPlaceholder");
    private string OpenInBrowserTooltipString => _stringLocalizer.GetString("WebView_UrlBar_OpenInBrowserTooltip");
    private string SettingsTooltipString => _stringLocalizer.GetString("WebView_UrlBar_SettingsTooltip");
    private string ManageBookmarksTooltipString => _stringLocalizer.GetString("WebView_Bookmarks_ManageTooltip");
    private string PlaceholderLoadFailedString => _stringLocalizer.GetString("WebView_Placeholder_LoadFailed");
    private string PlaceholderLoadFailedHintString => _stringLocalizer.GetString("WebView_Placeholder_LoadFailedHint");

    public WebViewDocumentView(
        IServiceProvider serviceProvider,
        ILogger<WebViewDocumentView> logger,
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

        ViewModel = serviceProvider.GetRequiredService<WebViewDocumentViewModel>();

        this.InitializeComponent();

        FindBar.Attach(this);
        FindBar.Closed += OnFindBarClosed;

        SettingsSurface.ReturnToPageRequested += SettingsSurface_ReturnToPageRequested;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.NavigateRequested += ViewModel_NavigateRequested;
        UpdateReloadOrStopTooltip();
        UpdateBookmarkPageButton();
        UpdatePlaceholderHint();

        Loaded += WebViewDocumentView_Loaded;
    }

    private void TryNavigate()
    {
        var sourceUrl = ViewModel.SourceUrl;
        if (string.IsNullOrEmpty(sourceUrl))
        {
            return;
        }

        Navigate(sourceUrl);
    }

    // Drops the page and returns the document to the placeholder it started on. A real navigation rather
    // than just clearing the address, so the old page stops running instead of playing on unseen.
    private void ClearPage()
    {
        Navigate("about:blank");
    }

    // Opens an address the user chose through the document, which the address bar names at once, as a
    // browser's does for an address typed into it.
    private void Navigate(string url)
    {
        var destination = ResolveDestination(url);
        if (destination is null)
        {
            return;
        }

        // Shown straight away rather than once the page commits. A document restored into a background tab
        // navigates while its view is out of the visual tree, and the Skia heads raise no navigation event
        // for it at all, not even once the tab is later shown, so its address bar would otherwise stay empty
        // for the life of the document. For the same reason the failure a previous navigation reported is
        // cleared here rather than in NavigationStarting alone.
        ViewModel.NotifyUserNavigation(destination.AbsoluteUri);

        LoadDestination(destination);
    }

    // Goes where the page asked to, as a link it followed does. The address bar goes on naming the page on
    // screen until the new one commits, so an address that turns out to be a download never shows in it.
    private void FollowPageNavigation(string url)
    {
        var destination = ResolveDestination(url);
        if (destination is null)
        {
            return;
        }

        LoadDestination(destination);
    }

    // The address a URL names, or null where it names none or there is no WebView to load it in.
    private Uri? ResolveDestination(string url)
    {
        if (_webView is null)
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination))
        {
            _logger.LogWarning($"Cannot navigate to invalid URL: '{url}'");
            return null;
        }

        return destination;
    }

    private void LoadDestination(Uri destination)
    {
        _webView?.Navigate(destination.AbsoluteUri);
    }

    private async void WebViewDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebViewDocumentView_Loaded;

        // Backstop for a view that reaches the visual tree without LoadContent having run.
        await EnsureWebViewInitializedAsync();
    }

    // Initialization runs once, from whichever of LoadContent and Loaded comes first. LoadContent is
    // awaited by the open command, so the web view exists by the time document_open returns rather than
    // whenever the tab happens to render.
    private async Task EnsureWebViewInitializedAsync()
    {
        _initializeWebViewTask ??= InitializeWebViewAsync();

        await _initializeWebViewTask;
    }

    private async Task InitializeWebViewAsync()
    {
        // An exception here must not escape: the caller is a Loaded handler on one path, so an
        // unobserved failure would crash the process rather than leaving an empty document.
        try
        {
            var webView = await _webViewFactory.AcquireAsync(CreateWebViewOptions());
            if (_isClosed)
            {
                webView.Dispose();
                return;
            }

            _webView = webView;
            webView.SetResource(FileResource);
            webView.SetAccessibleName(_accessibleName);

            webView.NavigationStarting += WebView_NavigationStarting;
            webView.NavigationCommitted += WebView_NavigationCommitted;
            webView.NavigationCompleted += WebView_NavigationCompleted;
            webView.NewWindowRequested += WebView_NewWindowRequested;
            webView.HistoryChanged += WebView_HistoryChanged;
            webView.DownloadStarted += WebView_DownloadStarted;
            webView.LoadedEmpty += WebView_LoadedEmpty;

            webView.AttachTo(AppWebViewContainer);

            // An external page runs no client script, so there is no DOM focus to release or grant. The
            // registry's native click monitor tracks focus on the page instead.
            RegisterWebSurfaceFocus(webView, releaseFocus: () => { });

            // No host RPC channel is opened. The page is untrusted third-party content, and the native message
            // bus is unauthenticated, so a channel would let the page drive host RPC methods.

            TryNavigate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView document view");
            TeardownWebViewState();
        }
    }

    private WebViewOptions CreateWebViewOptions()
    {
        // The macOS WKWebView default UA is otherwise flagged as an unsupported browser by some sites.
        var environmentInfo = _serviceProvider.GetRequiredService<IAppEnvironment>().GetEnvironmentInfo();

        return new WebViewOptions
        {
            // A page with no background of its own sits on white, as in a browser. On a transparent view, the
            // page's default black text would sit on the dark theme.
            BackgroundColor = Colors.White,
            IsDevToolsEnabled = _webViewService.IsDevToolsFeatureEnabled(),

            // The page is browsed content rather than application chrome, so user zoom stays enabled.
            IsZoomEnabled = true,
            UserAgentToken = $"Celbridge/{environmentInfo.AppVersion}",
        };
    }

    /// <summary>
    /// Disposes the web view. Safe to call more than once, and before the web view exists.
    /// </summary>
    private void TeardownWebViewState()
    {
        _webView?.Dispose();
        _webView = null;
    }

    private void WebView_HistoryChanged(object? sender, EventArgs e)
    {
        UpdateNavigationState();
    }

    private void WebView_NavigationCompleted(object? sender, WebNavigationCompletedEventArgs e)
    {
        ViewModel.NotifyNavigationCompleted(ResolveNavigationOutcome(e));
        UpdateNavigationState();
    }

    // A navigation that was cancelled or abandoned, such as one a later navigation superseded, one the user stopped
    // or one that became a download, is not a page that failed to load. The old page is still on screen, so the
    // placeholder would describe a failure that did not happen. A page that genuinely could not be fetched reports
    // why.
    private static NavigationOutcome ResolveNavigationOutcome(WebNavigationCompletedEventArgs e)
    {
        return e.Result switch
        {
            WebNavigationResult.Succeeded => NavigationOutcome.Loaded,
            WebNavigationResult.Failed => NavigationOutcome.Failed,
            _ => NavigationOutcome.Aborted
        };
    }

    // An empty page is reported as the failure it is, so the document shows the load-failed placeholder and its
    // reload rather than a blank page the user cannot tell from a slow one.
    private void WebView_LoadedEmpty(object? sender, EventArgs e)
    {
        ViewModel.NotifyNavigationCompleted(NavigationOutcome.Failed);
    }

    private void WebView_NavigationStarting(object? sender, WebNavigationStartingEventArgs args)
    {
        ViewModel.NotifyNavigationStarted(args.Uri);
    }

    // The address bar follows the page from here, as a browser's does: once a navigation commits, rather than
    // as it starts.
    private void WebView_NavigationCommitted(object? sender, string url)
    {
        ViewModel.NotifyNavigationCommitted(url);
    }

    // A navigation whose response turned out to be an attachment is abandoned for the download, and the page
    // the document is showing stays on screen.
    private void WebView_DownloadStarted(object? sender, EventArgs e)
    {
        ViewModel.NotifyDownloadStarted();
    }

    private void UpdateNavigationState()
    {
        if (_webView is null)
        {
            return;
        }

        ViewModel.CanGoBack = _webView.CanGoBack;
        ViewModel.CanGoForward = _webView.CanGoForward;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webView is not null &&
            _webView.CanGoBack)
        {
            _webView.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webView is not null &&
            _webView.CanGoForward)
        {
            _webView.GoForward();
        }
    }

    private async void ReloadOrStopButton_Click(object sender, RoutedEventArgs e)
    {
        var webView = _webView;
        if (webView is null)
        {
            return;
        }

        try
        {
            if (ViewModel.IsNavigating)
            {
                ViewModel.NotifyNavigationStopped();
                await webView.StopAsync();
            }
            else
            {
                // Reload acts on the page, not on the address box, so an uncommitted edit there is
                // dropped rather than left standing over a page it does not name.
                SyncAddressText();
                await webView.ReloadAsync(clearCache: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload or stop the page");
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryNormalizeUserUrl(ViewModel.SourceUrl, out var homeUrl))
        {
            // Navigating to the address already showing raises no change for the binding to follow, so an
            // uncommitted edit in the box would survive the navigation.
            SyncAddressText();

            // Home names a destination, so it gives the document area back to the page the way committing
            // an address does.
            ViewModel.CloseSettings();
            Navigate(homeUrl);
        }
    }

    // Puts the address the page is actually showing back in the box, discarding an edit the user typed
    // but never committed.
    private void SyncAddressText()
    {
        AddressTextBox.Text = ViewModel.AddressText;
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenBrowser(ViewModel.CurrentUrl);
    }

    private void ManageBookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        ShowBookmarksSection();
    }

    // Opens the card of the bookmark for the page on screen, bookmarking the page first when none points at it.
    private void BookmarkPageButton_Click(object sender, RoutedEventArgs e)
    {
        var bookmark = ViewModel.FindBookmarkForCurrentPage() ?? ViewModel.AddBookmarkFromCurrentPage();
        if (bookmark is null)
        {
            return;
        }

        ShowBookmarksSection();

        SettingsSurface.RevealBookmark(bookmark);
    }

    private void ShowBookmarksSection()
    {
        // Opening the settings builds them on the stored section, so the key is set first. A surface that
        // was already built ignores that key, and is sent to the section directly below.
        _settingsSectionKey = WebViewDocumentSettingsView.BookmarksSectionKey;
        ViewModel.IsSettingsOpen = true;

        SettingsSurface.SelectSection(WebViewDocumentSettingsView.BookmarksSectionKey);
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var bookmarkButton = (FrameworkElement)sender;
        if (bookmarkButton.DataContext is not WebViewBookmarkViewModel bookmark)
        {
            return;
        }

        ViewModel.OpenBookmark(bookmark);
    }

    // A bookmark, or anything else that opens a page without going through the address box.
    private void ViewModel_NavigateRequested(object? sender, string url)
    {
        // Navigating to the address already showing raises no change for the binding to follow, so an
        // uncommitted edit in the box would survive the navigation.
        SyncAddressText();
        Navigate(url);
        GiveFocusToWebContent();
    }

    private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Tab is here to move focus, and an address holds no tab or line break anyway.
        SingleLineText.RemoveTabsAndLineBreaks(AddressTextBox);
    }

    private void AddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // Committing an address is a request to see a page, so the settings give the document area back
            // rather than leaving the navigation to happen out of sight. An address that cannot be
            // navigated to leaves them where they are, having asked for nothing.
            var address = AddressTextBox.Text.Trim();
            if (address.Length == 0)
            {
                // Committing an empty address is a request to go nowhere, which leaves the document on
                // the placeholder rather than on the page it happened to be showing.
                ViewModel.CloseSettings();
                ClearPage();
            }
            else if (ViewModel.TryNormalizeUserUrl(address, out var url))
            {
                ViewModel.CloseSettings();
                Navigate(url);

                // Hand focus to the page the way the find bar does on close, so the next keystroke reaches
                // the content the user just navigated to and the panel focus reflects it.
                GiveFocusToWebContent();
            }

            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            var isEditing = AddressTextBox.Text != ViewModel.AddressText;

            // Abandon the edit, restoring the address the page is actually showing.
            SyncAddressText();

            // With no edit to abandon, Escape leaves the settings as it does anywhere else in the document.
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

    // A document with no way to navigate opens on its settings, whatever state it was saved in.
    private void OpenSettingsIfNoWayToNavigate()
    {
        if (ViewModel.HasWayToNavigate)
        {
            return;
        }

        _settingsSectionKey = WebViewDocumentSettingsView.HomeSectionKey;
        ViewModel.IsSettingsOpen = true;

        // The open state may be unchanged, which raises no property change to drive the layout.
        ApplyContentLayout();
    }

    // A document showing nothing navigates as soon as it is given an address. One already showing a page
    // keeps it: changing the Home URL is not a request to leave the page.
    private void NavigateIfPageIsBlank()
    {
        if (ViewModel.HasPage)
        {
            return;
        }

        TryNavigate();
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

    // Gives the document area to whichever of the page, the settings and the placeholder belongs there.
    // The WebView is collapsed rather than covered by either of the other two: a hosted web view is a
    // native view above the canvas they are drawn on, so while it is shown it takes mouse input meant for
    // them, and the cursor over it answers to the page.
    private void ApplyContentLayout()
    {
        var showSettings = ViewModel.IsSettingsOpen;
        if (showSettings)
        {
            SettingsSurface.Initialize(ViewModel, _settingsSectionKey);

            // Find applies to the page, which the settings hide.
            if (FindBar.Visibility == Visibility.Visible)
            {
                FindBar.Close();
            }
        }

        SettingsSurface.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        ContentPlaceholder.Visibility = ViewModel.IsPlaceholderVisible ? Visibility.Visible : Visibility.Collapsed;
        AppWebViewContainer.Visibility = ViewModel.IsPageOnScreen ? Visibility.Visible : Visibility.Collapsed;

        UpdatePlaceholderName();

        // A hidden page passes the keyboard on, since on macOS a hidden web view holding it keeps every keystroke.
        if (!ViewModel.IsPageOnScreen
            && _webView is not null
            && _webViewFocusRegistry.IsFocusedSurface(_webView))
        {
            FocusDocumentContent();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WebViewDocumentViewModel.IsNavigating))
        {
            UpdateReloadOrStopTooltip();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.IsCurrentPageBookmarked))
        {
            UpdateBookmarkPageButton();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.ShowUrlBar)
            || e.PropertyName == nameof(WebViewDocumentViewModel.IsBookmarksBarVisible))
        {
            UpdatePlaceholderHint();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.IsSettingsOpen))
        {
            ApplyContentLayout();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.CurrentUrl)
            || e.PropertyName == nameof(WebViewDocumentViewModel.HasNavigationFailed))
        {
            ApplyContentLayout();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.SourceUrl))
        {
            NavigateIfPageIsBlank();
        }
    }

    private void UpdateReloadOrStopTooltip()
    {
        var key = ViewModel.IsNavigating ? "WebView_UrlBar_StopTooltip" : "WebView_UrlBar_ReloadTooltip";
        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(ReloadOrStopButton, tooltip);
        AutomationProperties.SetName(ReloadOrStopButton, tooltip);
    }

    private void UpdateBookmarkPageButton()
    {
        var isBookmarked = ViewModel.IsCurrentPageBookmarked;

        BookmarkPageIcon.Symbol = isBookmarked ? IconSymbol.StarFilled : IconSymbol.Star;

        var key = isBookmarked ? "WebView_UrlBar_EditBookmarkTooltip" : "WebView_UrlBar_BookmarkPageTooltip";
        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(BookmarkPageButton, tooltip);
        AutomationProperties.SetName(BookmarkPageButton, tooltip);
    }

    private void UpdatePlaceholderHint()
    {
        var showUrlBar = ViewModel.ShowUrlBar;
        var showBookmarksBar = ViewModel.IsBookmarksBarVisible;

        string key;
        if (showUrlBar && showBookmarksBar)
        {
            key = "WebView_Placeholder_AddressOrBookmarkHint";
        }
        else if (showUrlBar)
        {
            key = "WebView_Placeholder_AddressHint";
        }
        else if (showBookmarksBar)
        {
            key = "WebView_Placeholder_BookmarkHint";
        }
        else
        {
            key = "WebView_Placeholder_SettingsHint";
        }

        PlaceholderHint.Text = _stringLocalizer.GetString(key);

        UpdatePlaceholderName();
    }

    // The placeholder takes the keyboard when nothing else in the document can, so it is named for what it says.
    private void UpdatePlaceholderName()
    {
        string name;
        if (ViewModel.IsLoadFailedVisible)
        {
            name = PlaceholderLoadFailedString;
        }
        else
        {
            name = PlaceholderHint.Text;
        }

        AutomationProperties.SetName(PlaceholderContent, name);
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var setResult = await base.SetFileResource(fileResource);
        if (setResult.IsFailure)
        {
            return setResult;
        }

        // A rename reuses this view, so its web view follows the resource.
        _webView?.SetResource(FileResource);

        return setResult;
    }

    public override async Task<Result> LoadContent()
    {
        var loadResult = await ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        OpenSettingsIfNoWayToNavigate();

        // Runs after the view model so the Home URL is read by the time initialization navigates.
        var wasInitialized = _initializeWebViewTask is not null;
        await EnsureWebViewInitializedAsync();

        if (wasInitialized)
        {
            // A rename reloads the same view, so the URL initialization already navigated to is stale.
            TryNavigate();
        }

        return loadResult;
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

        // A view that has not finished initializing would report a default settings state, which the
        // layout store would then write over good saved state.
        if (_webView is null)
        {
            return null;
        }

        var sectionKey = SettingsSurface.SelectedSectionKey;
        if (string.IsNullOrEmpty(sectionKey))
        {
            // The surface builds its sections on first use, so a document that never opened the settings
            // carries the section it was restored with rather than reporting none.
            sectionKey = _settingsSectionKey;
        }

        var editorState = new WebViewEditorState(ViewModel.IsSettingsOpen, sectionKey);

        return JsonSerializer.Serialize(editorState, EditorStateSerializerOptions);
    }

    public override WebViewHealth GetHealth()
    {
        return _webView?.GetHealth() ?? WebViewHealth.Healthy;
    }

    public override async Task RestoreEditorStateAsync(string state)
    {
        await Task.CompletedTask;

        WebViewEditorState? editorState;
        try
        {
            editorState = JsonSerializer.Deserialize<WebViewEditorState>(state, EditorStateSerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to restore the WebView document editor state");
            return;
        }

        if (editorState is null)
        {
            return;
        }

        _settingsSectionKey = editorState.SettingsSectionKey;
        ViewModel.IsSettingsOpen = editorState.SettingsOpen;

        // The open state may be unchanged from the default, which raises no property change, so apply the
        // layout directly rather than relying on the view model notification.
        ApplyContentLayout();

        // Restoring runs after the content loads, so a saved closed state would otherwise put a document
        // with no way in back to its blank page.
        OpenSettingsIfNoWayToNavigate();
    }

    // The URL bar is the only chrome this view hides, and it carries every control the view owns.
    public bool CanRestoreChrome => !ViewModel.ShowUrlBar;

    public string RestoreChromeMenuTextKey => "WebView_ShowUrlBar";

    public void RestoreChrome()
    {
        ViewModel.ShowUrlBar = true;
    }

    // A browser document has no tabs, so it opens the address itself, and Back returns. The URL bar button is
    // the way to the system browser, which also keeps downloads in the project. The page asked for the window,
    // so this counts as the page's own navigation.
    private void WebView_NewWindowRequested(object? sender, string url)
    {
        FollowPageNavigation(url);
    }

    public override IEditTarget EditTarget { get; } = new PlatformEditTarget();

    public override void SetAccessibleName(string name)
    {
        _accessibleName = name;
        _webView?.SetAccessibleName(name);
    }

    public override void FocusDocument()
    {
        FocusDocumentContent();
    }

    // Gives the keyboard to whatever fills the document area, or failing that to whatever way in the
    // placeholder offers. A document being opened has already started navigating to its Home URL by the
    // time it is activated and focused, so its page counts as on screen.
    private void FocusDocumentContent()
    {
        if (ViewModel.IsSettingsOpen)
        {
            if (!SettingsSurface.FocusRail())
            {
                _logger.LogDebug("The Web View settings rail did not take focus");
            }

            return;
        }

        if (ViewModel.IsPageOnScreen)
        {
            GiveFocusToWebContent();
            return;
        }

        if (ViewModel.ShowUrlBar)
        {
            AddressTextBox.Focus(FocusState.Programmatic);
            return;
        }

        if (ViewModel.IsBookmarksBarVisible
            && FocusNavigationHelper.TryFocusFirstElement(BookmarksBar))
        {
            return;
        }

        // Taken even with nothing to act on, so the keyboard does not stay with whatever held it before.
        PlaceholderContent.Focus(FocusState.Programmatic);
    }

    // Call it only for a page that is on screen, or one a navigation has just started to show. On macOS,
    // native focus lands on a hidden web view all the same, and holds every keystroke.
    private void GiveFocusToWebContent()
    {
        if (_webView is null)
        {
            _logger.LogWarning("Cannot focus the page before its WebView is created");
            return;
        }

        _webViewFocusRegistry.GrantFocus(_webView);
    }

    // True when the host find bar can drive this document: the page is the thing on screen, the WebView is
    // live, and its backend has no find UI of its own (the Windows Chromium heads do, so they report false
    // and keep their built-in bar).
    public override bool CanFind => ViewModel.IsPageOnScreen
        && _webView is not null
        && !_webView.ProvidesBuiltInFind;

    public override bool TryBeginFind()
    {
        if (!CanFind)
        {
            return false;
        }

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

    async Task IWebViewFindTarget.StartFindAsync(string term, FindOptions options)
    {
        if (_webView is not null)
        {
            await _webView.StartFindAsync(term, options);
        }
    }

    void IWebViewFindTarget.FindNext()
    {
        _webView?.FindNext();
    }

    void IWebViewFindTarget.FindPrevious()
    {
        _webView?.FindPrevious();
    }

    void IWebViewFindTarget.StopFind()
    {
        _webView?.StopFind();
    }

    public override async Task PrepareToClose()
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.NavigateRequested -= ViewModel_NavigateRequested;

        _isClosed = true;
        TeardownWebViewState();

        await base.PrepareToClose();
    }
}
