using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;
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
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;

    private WebView2? _webView;

    private int _processFailures;
    // Set on the first initialization attempt, so LoadContent and Loaded share a single run.
    private Task? _initializeWebViewTask;
    private IWebViewDownloadHandler? _downloadHandler;

    // Reports each address the page commits to, which is when the address bar follows the page.
    private IDisposable? _navigationCommits;

    // The section the settings reopen on, carried until the surface is built on first use.
    private string _settingsSectionKey = string.Empty;

    // Set when a download replaces the navigation in flight, which then reports itself failed.
    private bool _isNavigationReplacedByDownload;

    private WebViewLoadDiagnostics? _diagnostics;

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
        _webViewAdapter = ServiceLocator.AcquireService<IWebViewAdapter>();
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
        if (_webView is null)
        {
            return;
        }

        // Paired with the completion below, so a page that never arrives can be told from one that arrived
        // and failed.
        Diagnostics.LogNavigation("Navigating", Surface, destination.AbsoluteUri);

        _webView.Source = destination;
    }

    private async void WebViewDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebViewDocumentView_Loaded;

        // Backstop for a view that reaches the visual tree without LoadContent having run.
        await EnsureWebViewInitializedAsync();
    }

    // Initialization runs once, from whichever of LoadContent and Loaded comes first. LoadContent is
    // awaited by the open command, so the WebView and its webview_* tool bridge registration exist by
    // the time document_open returns rather than whenever the tab happens to render.
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
            _webView = await _webViewFactory.AcquireAsync();
            AppWebViewContainer.Children.Add(_webView);

            // Attach and detach are what a tab switch does to the surface, so both are logged with the state
            // they leave it in. A navigation that starts on its own after one is the page being reloaded.
            _webView.Loaded += WebView_Loaded;
            _webView.Unloaded += WebView_Unloaded;

            // An external page runs no client script, so there is no DOM focus to release or grant. The
            // registry's native click monitor tracks focus on the page instead.
            RegisterWebSurfaceFocus(_webView, releaseFocus: () => { });

            var devToolsEnabled = _webViewService.IsDevToolsFeatureEnabled();
            _webViewAdapter.SetDevToolsEnabled(_webView.CoreWebView2, devToolsEnabled, FileResource.ResourceName);

            // The page is browsed content rather than application chrome, so user zoom stays enabled.
            _webViewAdapter.SetZoomControlEnabled(_webView.CoreWebView2, true);
            // The macOS WKWebView default UA is otherwise flagged as an unsupported browser by some sites.
            var environmentInfo = _serviceProvider.GetRequiredService<IAppEnvironment>().GetEnvironmentInfo();
            _webViewAdapter.SetApplicationUserAgent(_webView.CoreWebView2, $"Celbridge/{environmentInfo.AppVersion}");

            // No host RPC channel is opened. The page is untrusted third-party content, and the native message
            // bus is unauthenticated, so a channel would let the page drive host RPC methods.

            DetachDownloadHandler();
            _downloadHandler = _webViewAdapter.AttachDownloadHandler(_webView.CoreWebView2);
            _downloadHandler.DownloadStarted += CoreWebView2_DownloadStarted;

            _navigationCommits?.Dispose();
            _navigationCommits = _webViewAdapter.ObserveNavigationCommits(_webView.CoreWebView2, OnNavigationCommitted);

            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;

            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            _webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

            // Raised only by the packaged Windows head's WebView2. The Skia heads report a dead renderer
            // through the web view adapter instead.
            _webView.CoreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
            _webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;

            TryNavigate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView document view");
            TeardownWebViewState();
        }
    }

    /// <summary>
    /// Tears down the WebView and its event handlers. Safe to call multiple times and from partially
    /// initialized states.
    /// </summary>
    private void TeardownWebViewState()
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webViewFocusRegistry.Unregister(_webView.CoreWebView2);

            DetachDownloadHandler();

            _navigationCommits?.Dispose();
            _navigationCommits = null;

            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            _webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
        }

        if (_webView is not null)
        {
            _webView.Loaded -= WebView_Loaded;
            _webView.Unloaded -= WebView_Unloaded;

            _webViewAdapter.CloseWebView(_webView, AppWebViewContainer);

            _webView = null;
        }
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        UpdateNavigationState();
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _processFailures++;

        _logger.LogError(
            "WebView ProcessFailed: Kind={Kind}, Reason={Reason}, ExitCode={ExitCode}",
            e.ProcessFailedKind, e.Reason, e.ExitCode);
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var outcome = ResolveNavigationOutcome(e, _isNavigationReplacedByDownload);
        _isNavigationReplacedByDownload = false;

        // A navigation that never arrived is logged by where it was heading, since the address bar still names
        // the page it left.
        if (outcome == NavigationOutcome.Failed)
        {
            Diagnostics.LogNavigationFailed(Surface, ViewModel.NavigationDestination, e.WebErrorStatus);
        }
        else if (outcome == NavigationOutcome.Aborted)
        {
            Diagnostics.LogNavigation("Navigation abandoned", Surface, ViewModel.NavigationDestination);
        }
        else
        {
            Diagnostics.LogNavigation("Navigation completed", Surface, ViewModel.CurrentUrl);
        }

        ViewModel.NotifyNavigationCompleted(outcome);
        UpdateNavigationState();

        // Runs after the navigation state settles so the probe reads the address the page committed to.
        if (e.IsSuccess)
        {
            _ = ProbeLoadedContentAsync();
        }
    }

    // Chromium reports three cases the same way: a navigation it turned into a download, one a later
    // navigation superseded, and one the user stopped. None is a page that failed to load, and in each the
    // old page is still on screen, so the placeholder would describe a failure that did not happen. A page
    // that genuinely could not be fetched reports why. The macOS head reports every failure alike, but
    // there the download is announced before the navigation it replaced ends, so that one is already known
    // to be abandoned.
    private static NavigationOutcome ResolveNavigationOutcome(
        CoreWebView2NavigationCompletedEventArgs e,
        bool isReplacedByDownload)
    {
        if (e.IsSuccess)
        {
            return NavigationOutcome.Loaded;
        }

        if (isReplacedByDownload
            || e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted
            || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return NavigationOutcome.Aborted;
        }

        return NavigationOutcome.Failed;
    }

    // The load diagnostics shared with the custom editor controller: the surface a load runs against, and
    // the probe of what a completed navigation actually produced.
    private WebViewLoadDiagnostics Diagnostics => _diagnostics ??= new WebViewLoadDiagnostics(
        _webViewAdapter,
        _serviceProvider.GetRequiredService<IFeatureFlags>(),
        _logger);

    private WebViewSurface Surface => new(FileResource.ToString(), _webView);

    private async Task ProbeLoadedContentAsync()
    {
        // The blank page a document rests on between addresses is empty by design.
        var probedUrl = ViewModel.CurrentUrl;
        if (string.IsNullOrEmpty(probedUrl)
            || probedUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var probe = await Diagnostics.ProbeAsync(Surface);
        if (probe is null)
        {
            return;
        }

        // The probe reports on a completion that has already happened, so a navigation started while it
        // was in flight owns the document now, and this verdict is about a page already gone.
        if (ViewModel.IsNavigating
            || !string.Equals(ViewModel.CurrentUrl, probedUrl, StringComparison.Ordinal))
        {
            return;
        }

        Diagnostics.LogProbe(Surface, probedUrl, probe);

        if (probe.IsEmpty)
        {
            // Reported as the failure it is, so the document shows the load-failed placeholder and its reload
            // rather than a blank page the user cannot tell from a slow one.
            ViewModel.NotifyNavigationCompleted(NavigationOutcome.Failed);
        }
    }

    private void WebView_Loaded(object sender, RoutedEventArgs e)
    {
        _ = Diagnostics.LogSurfaceAsync("WebView attached", Surface);

        // A document that loaded while detached raised no navigation events, so its completion was never
        // probed. Attach is the first moment the host hears from it again.
        _ = ProbeLoadedContentAsync();
    }

    private void WebView_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = Diagnostics.LogSurfaceAsync("WebView detached", Surface);

        // Unloaded fires before Uno has taken the native view apart, so the state the surface is left in
        // while the tab is away is only readable once that work has run.
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { _ = Diagnostics.LogSurfaceAsync("WebView detached, settled", Surface); });
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // A start with no Navigating line before it is the page reloading on its own, which is what a
        // redirect looks like and what a re-attach must not.
        Diagnostics.LogNavigation("Navigation starting", Surface, args.Uri);

        _isNavigationReplacedByDownload = false;

        ViewModel.NotifyNavigationStarted(args.Uri ?? string.Empty);
    }

    // The address bar follows the page from here, as a browser's does: once a navigation commits, rather than
    // as it starts.
    private void OnNavigationCommitted(string url)
    {
        Diagnostics.LogNavigation("Navigation committed", Surface, url);

        ViewModel.NotifyNavigationCommitted(url);
    }

    // A navigation whose response turned out to be an attachment is abandoned for the download, and the page
    // the document is showing stays on screen.
    private void CoreWebView2_DownloadStarted(object? sender, EventArgs e)
    {
        // Chromium has already ended the navigation by now. WebKit has not, and ends it with a failure. A
        // navigation that started while the view was detached reported no start, so this is not gated on
        // one being known to be in flight.
        _isNavigationReplacedByDownload = true;

        ViewModel.NotifyDownloadStarted();
    }

    private void DetachDownloadHandler()
    {
        if (_downloadHandler is null)
        {
            return;
        }

        _downloadHandler.DownloadStarted -= CoreWebView2_DownloadStarted;
        _downloadHandler.Detach();
        _downloadHandler = null;
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
        if (_webView?.CoreWebView2 is not CoreWebView2 coreWebView2)
        {
            return;
        }

        try
        {
            if (ViewModel.IsNavigating)
            {
                // Unlike the other navigation commands, Stop has no equivalent on the WebView2 control, and
                // the CoreWebView2 member behind it is unimplemented on the Skia heads, so it goes through
                // the adapter.
                ViewModel.NotifyNavigationStopped();
                await _webViewAdapter.StopAsync(coreWebView2);
            }
            else
            {
                // Reload acts on the page, not on the address box, so an uncommitted edit there is
                // dropped rather than left standing over a page it does not name.
                SyncAddressText();
                await _webViewAdapter.ReloadAsync(coreWebView2, clearCache: true);
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
    }

    private void UpdateBookmarkPageButton()
    {
        var isBookmarked = ViewModel.IsCurrentPageBookmarked;

        BookmarkPageIcon.Symbol = isBookmarked ? IconSymbol.StarFilled : IconSymbol.Star;

        var key = isBookmarked ? "WebView_UrlBar_EditBookmarkTooltip" : "WebView_UrlBar_BookmarkPageTooltip";
        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(BookmarkPageButton, tooltip);
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

    public override DocumentHealth GetHealth()
    {
        var coreWebView2 = _webView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            return new DocumentHealth(0, _processFailures);
        }

        var pageHealth = _webViewAdapter.GetHostedPageHealth(coreWebView2);
        return pageHealth with { ProcessFailures = pageHealth.ProcessFailures + _processFailures };
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

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        var url = args.Uri;
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        // A browser document has no tabs to open, so it goes there itself and Back returns. Leaving for
        // the system browser is what the URL bar button is for, and taking a download with it would put
        // the file outside the project. The page asked for the window, so it is followed as the page's own
        // navigation.
        FollowPageNavigation(url);
    }

    public override IEditTarget EditTarget { get; } = new DisabledEditTarget();

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
        && !_webViewAdapter.ProvidesBuiltInFind
        && _webView?.CoreWebView2 is not null;

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
        if (_webView?.CoreWebView2 is CoreWebView2 coreWebView2)
        {
            await _webViewAdapter.StartFindAsync(coreWebView2, term, options);
        }
    }

    void IWebViewFindTarget.FindNext()
    {
        if (_webView?.CoreWebView2 is CoreWebView2 coreWebView2)
        {
            _webViewAdapter.FindNext(coreWebView2);
        }
    }

    void IWebViewFindTarget.FindPrevious()
    {
        if (_webView?.CoreWebView2 is CoreWebView2 coreWebView2)
        {
            _webViewAdapter.FindPrevious(coreWebView2);
        }
    }

    void IWebViewFindTarget.StopFind()
    {
        if (_webView?.CoreWebView2 is CoreWebView2 coreWebView2)
        {
            _webViewAdapter.StopFind(coreWebView2);
        }
    }

    public override async Task PrepareToClose()
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.NavigateRequested -= ViewModel_NavigateRequested;

        TeardownWebViewState();

        await base.PrepareToClose();
    }
}
