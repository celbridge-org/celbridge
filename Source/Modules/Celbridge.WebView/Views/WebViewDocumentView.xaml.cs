using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace Celbridge.WebView.Views;

/// <summary>
/// The per-user view state of a .webview document: whether the settings are open and which section they
/// are showing. Persisted through the document editor state, not the .webview file.
/// </summary>
internal sealed record WebViewEditorState(bool SettingsOpen, string SettingsSectionKey);

/// <summary>
/// Hosts an arbitrary user URL from a .webview document, or a project-served
/// HTML page from a .html / .htm document. The two roles share a single WebView2
/// lifecycle and differ only in URL source, navigation policy, and chrome: the
/// external-URL role presents a browser-style URL bar above the page and a
/// resizable settings panel over it.
/// </summary>
public sealed partial class WebViewDocumentView : DocumentView, IHostInput, IFindableDocument, IWebViewFindTarget, IDocumentChromeOwner
{
    // How long the settled download indicator stays visible before fading out.
    private static readonly TimeSpan DownloadIndicatorDismissDelay = TimeSpan.FromSeconds(10);

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
    // Set on the first initialization attempt, so LoadContent and Loaded share a single run.
    private Task? _initializeWebViewTask;
    // Host RPC channel. Only created for the HtmlViewer role. External-URL documents run without one.
    private WebViewHostChannel? _hostChannel;
    private CelbridgeHost? _host;
    private IWebViewNavigationPolicy? _navigationPolicy;

    private DispatcherTimer? _downloadIndicatorDismissTimer;

    // The section the settings reopen on, carried until the surface is built on first use.
    private string _settingsSectionKey = string.Empty;

    // Where the page was last told to go, held until the committed address catches up with it.
    private string _pendingNavigationUrl = string.Empty;

    private WebViewLoadDiagnostics? _diagnostics;

    // Set on successful registration with the bridge. Only populated for the
    // HtmlViewer role. .webview (external URL) documents do not register and the
    // webview_* tool namespace is not supported for them.
    private IDocumentWebViewToolBridge? _toolBridge;

    private static readonly WebViewDocumentOptions DefaultOptions = new(
        WebViewDocumentRole.ExternalUrl,
        InterceptTopFrameNavigation: false);

    /// <summary>
    /// Per-instance options supplied by the editor factory. Defaults to the .webview
    /// external-URL behaviour.
    /// </summary>
    internal WebViewDocumentOptions Options { get; set; } = DefaultOptions;

    public WebViewDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    private string BackTooltipString => _stringLocalizer.GetString("WebView_UrlBar_BackTooltip");
    private string ForwardTooltipString => _stringLocalizer.GetString("WebView_UrlBar_ForwardTooltip");
    private string HomeTooltipString => _stringLocalizer.GetString("WebView_UrlBar_HomeTooltip");
    private string AddressPlaceholderString => _stringLocalizer.GetString("WebView_UrlBar_AddressPlaceholder");
    private string OpenInBrowserTooltipString => _stringLocalizer.GetString("WebView_UrlBar_OpenInBrowserTooltip");
    private string SettingsTooltipString => _stringLocalizer.GetString("WebView_UrlBar_SettingsTooltip");
    private string ManageBookmarksTooltipString => _stringLocalizer.GetString("WebView_Bookmarks_ManageTooltip");
    private string PlaceholderAddressHintString => _stringLocalizer.GetString("WebView_Placeholder_AddressHint");
    private string PlaceholderSettingsHintString => _stringLocalizer.GetString("WebView_Placeholder_SettingsHint");
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

        Loaded += WebViewDocumentView_Loaded;
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    private void TryNavigate()
    {
        var navigateUrl = ViewModel.NavigateUrl;
        if (string.IsNullOrEmpty(navigateUrl))
        {
            return;
        }

        Navigate(navigateUrl);
    }

    // Drops the page and returns the document to the placeholder it started on. A real navigation rather
    // than just clearing the address, so the page being left stops running instead of playing on unseen.
    private void ClearPage()
    {
        Navigate("about:blank");
    }

    private void Navigate(string url)
    {
        if (_webView is null)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning($"Cannot navigate to invalid URL: '{url}'");
            return;
        }

        // Show the destination straight away rather than waiting for NavigationStarting to report it. A
        // document restored into a background tab navigates while its view is out of the visual tree, and
        // the Skia heads raise no navigation event for it at all, not even once the tab is later shown, so
        // its address bar would otherwise stay empty for the life of the document. Any redirect is picked
        // up by the navigation events as usual.
        // A document restored into a background tab navigates with no events at all, so the failure a
        // previous navigation reported is cleared here rather than in NavigationStarting alone.
        ViewModel.HasNavigationFailed = false;

        ViewModel.CurrentUrl = uri.AbsoluteUri;
        _pendingNavigationUrl = uri.AbsoluteUri;

        // Paired with the completion below, so a page that never arrives can be told from one that arrived
        // and failed, and from one the policy declined.
        Diagnostics.LogNavigation("Navigating", Surface, uri.AbsoluteUri);

        _webView.Source = uri;
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

            // The DOM focus callbacks only reach a page that loads the client script. An external-URL page
            // relies on the registry's native click monitor instead.
            RegisterWebSurfaceFocus(_webView, ReleaseFocus, GrantDomFocusAsync);

            var devToolsEnabled = _webViewService.IsDevToolsFeatureEnabled();
            _webViewAdapter.SetDevToolsEnabled(_webView.CoreWebView2, devToolsEnabled, FileResource.ResourceName);

            // The .webview browser and HTML viewer render page content, so keep user zoom enabled.
            _webViewAdapter.SetZoomControlEnabled(_webView.CoreWebView2, true);
            // The macOS WKWebView default UA is otherwise flagged as an unsupported browser by some sites.
            var environmentInfo = _serviceProvider.GetRequiredService<IAppEnvironment>().GetEnvironmentInfo();
            _webViewAdapter.SetApplicationUserAgent(_webView.CoreWebView2, $"Celbridge/{environmentInfo.AppVersion}");

            // Only the HtmlViewer role runs a host RPC channel. External-URL .webview documents load
            // untrusted third-party content: the native message bus is unauthenticated, so a channel
            // there would let a page drive host RPC methods.
            if (Options.Role == WebViewDocumentRole.HtmlViewer)
            {
                // The HTML viewer renders loopback project content and supports the webview_* MCP tools.
                await TryInjectToolBridgeShimAsync();

                var webSurfaceLog = ServiceLocator.AcquireService<IWebSurfaceLog>();
                var logTarget = new WebSurfaceLogTarget(FileResource.ToString(), webSurfaceLog);

                _hostChannel = new WebViewHostChannel(_webView.CoreWebView2);
                _host = new CelbridgeHost(_hostChannel, logTarget);
                _host.AddLocalRpcTarget<IHostInput>(this);
                _host.StartListening();

                TryRegisterWithToolBridge();
            }

            // temp:/ is wiped on workspace load, so the downloads sub-folder
            // may not exist yet. Ensure it via the gateway before the user
            // can trigger a download.
            var downloadsFolder = new ResourceKey($"temp:{ProjectConstants.DownloadsFolder}");
            await ResourceFileSystem.CreateFolderAsync(downloadsFolder);

            _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
            _webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;

            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            _webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

            AttachNavigationPolicy(_webView.CoreWebView2);

            TryNavigate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView document view");
            TeardownWebViewState();
        }
    }

    private void AttachNavigationPolicy(CoreWebView2 coreWebView)
    {
        _navigationPolicy = _serviceProvider.GetRequiredService<IWebViewNavigationPolicy>();

        NavigationDestinationHandler handler;
        if (Options.InterceptTopFrameNavigation)
        {
            handler = CreateInterceptingHandler();
        }
        else
        {
            handler = (_) => Task.FromResult(NavigationDecision.Allow);
        }

        _navigationPolicy.Attach(coreWebView, handler);
    }

    private NavigationDestinationHandler CreateInterceptingHandler()
    {
        return async (destination) =>
        {
            // The HTML viewer is pinned to the project virtual-host URL. Allow the
            // initial navigation, reloads, and any same-document scrolling, but prompt
            // the user for any other top-frame destination so the page cannot redirect
            // out from under them.
            var pinnedUrl = ViewModel.NavigateUrl;
            if (!string.IsNullOrEmpty(pinnedUrl) && IsSameDocument(destination, pinnedUrl))
            {
                return NavigationDecision.Allow;
            }

            return await PromptForNavigationDestinationAsync(destination);
        };
    }

    private static bool IsSameDocument(Uri destination, string pinnedUrl)
    {
        if (!Uri.TryCreate(pinnedUrl, UriKind.Absolute, out var pinned))
        {
            return false;
        }

        return string.Equals(destination.Scheme, pinned.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(destination.Host, pinned.Host, StringComparison.OrdinalIgnoreCase)
            && destination.Port == pinned.Port
            && string.Equals(destination.AbsolutePath, pinned.AbsolutePath, StringComparison.Ordinal);
    }

    private async Task<NavigationDecision> PromptForNavigationDestinationAsync(Uri destination)
    {
        try
        {
            var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
            var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();

            var title = stringLocalizer.GetString("WebView_NavigationPrompt_Title");
            var message = stringLocalizer.GetString("WebView_NavigationPrompt_Message", destination.ToString());
            var openInBrowserOption = stringLocalizer.GetString("WebView_NavigationPrompt_OpenInBrowser");

            var options = new List<string> { openInBrowserOption };

            var dialogResult = await dialogService.ShowChoiceDialogAsync(title, message, options, defaultIndex: 0);
            if (dialogResult.IsFailure)
            {
                return NavigationDecision.Cancel;
            }

            var choice = dialogResult.Value;
            if (choice.SelectedIndex == 0)
            {
                return NavigationDecision.OpenInSystemBrowser;
            }

            return NavigationDecision.Cancel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prompt for navigation destination");
            return NavigationDecision.Cancel;
        }
    }

    /// <summary>
    /// Tears down the WebView, host channel, and associated event handlers. Safe
    /// to call multiple times and from partially initialized states.
    /// </summary>
    private void TeardownWebViewState()
    {
        if (_toolBridge is not null)
        {
            _toolBridge.Unregister(FileResource);
            _toolBridge = null;
        }

        if (_webView?.CoreWebView2 is not null)
        {
            _webViewFocusRegistry.Unregister(_webView.CoreWebView2);

            _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            _webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;

            if (_navigationPolicy is not null)
            {
                _navigationPolicy.Detach(_webView.CoreWebView2);
            }
        }

        if (_webView is not null)
        {
            _webView.Loaded -= WebView_Loaded;
            _webView.Unloaded -= WebView_Unloaded;

            _webViewAdapter.CloseWebView(_webView, AppWebViewContainer);

            _webView = null;
        }

        _navigationPolicy = null;

        _host?.Dispose();
        _hostChannel?.Detach();

        _host = null;
        _hostChannel = null;
    }

    private async Task TryInjectToolBridgeShimAsync()
    {
        var coreWebView2 = _webView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            return;
        }

        var toolBridge = _serviceProvider.GetService<IDocumentWebViewToolBridge>();
        if (toolBridge is null)
        {
            return;
        }

        // Install the shim as a document-start script so it wraps console/fetch before page scripts run,
        // required for get_console / get_network capture. Running before the first navigation captures the
        // initial page's boot output.
        try
        {
            var script = toolBridge.GetShimScript();
            await _webViewAdapter.InstallDocumentStartScriptAsync(coreWebView2, script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install the document-start WebView tool bridge shim into the HTML viewer");
        }
    }

    private async Task ReinjectToolBridgeShimAsync()
    {
        var coreWebView2 = _webView?.CoreWebView2;
        if (coreWebView2 is null || _toolBridge is null)
        {
            return;
        }

        try
        {
            var script = _toolBridge.GetShimScript();
            await _webViewAdapter.ReinjectDocumentStartScriptAsync(coreWebView2, script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-inject the WebView tool bridge shim");
        }
    }

    private void TryRegisterWithToolBridge()
    {
        var webView = _webView;
        if (webView?.CoreWebView2 is null)
        {
            return;
        }

        var toolBridge = _serviceProvider.GetService<IDocumentWebViewToolBridge>();
        if (toolBridge is null)
        {
            return;
        }

        var resource = FileResource;
        if (resource.IsEmpty)
        {
            return;
        }

        toolBridge.RegisterWebView2(resource, webView, _webViewAdapter);

        _toolBridge = toolBridge;
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        UpdateNavigationState();
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // The HTML viewer renders static project-served content, so the WebView's own
        // NavigationCompleted is a sufficient content-ready signal. External-URL .webview
        // documents never register, so this no-ops on the .webview path.
        if (Options.Role == WebViewDocumentRole.HtmlViewer)
        {
            if (e.IsSuccess)
            {
                // Re-deliver the shim before opening the content-ready gate (no-op on Windows). ExecuteScriptAsync
                // calls are serialised in invocation order, so this fire-and-forget eval is queued ahead of any
                // later webview_* tool eval even without awaiting it here.
                _ = ReinjectToolBridgeShimAsync();
                _toolBridge?.NotifyContentReady(FileResource);
            }
            else
            {
                var reason = $"The WebView navigation failed with status '{e.WebErrorStatus}'.";
                _toolBridge?.NotifyContentFailed(FileResource, reason);
            }
        }

        if (e.IsSuccess)
        {
            Diagnostics.LogNavigation("Navigation completed", Surface, ViewModel.CurrentUrl);
        }
        else
        {
            Diagnostics.LogNavigationFailed(Surface, ViewModel.CurrentUrl, e.WebErrorStatus);
        }

        ViewModel.NotifyNavigationCompleted(e.IsSuccess);
        UpdateNavigationState();

        // Runs after the navigation state settles so the probe reads the address the page committed to.
        if (e.IsSuccess)
        {
            _ = ProbeLoadedContentAsync();
        }
    }

    // The load diagnostics shared with the custom editor controller: the surface a load runs against, and
    // the probe of what a completed navigation actually produced.
    private WebViewLoadDiagnostics Diagnostics => _diagnostics ??= new WebViewLoadDiagnostics(
        _webViewAdapter,
        _serviceProvider.GetRequiredService<IFeatureFlags>(),
        _logger);

    // Only the external-URL role treats an empty document as a failed load: it owns the placeholder that
    // reports one, and a project-served page can legitimately be empty.
    private WebViewSurface Surface => new(
        FileResource.ToString(),
        _webView,
        TreatEmptyDocumentAsFailure: Options.Role == WebViewDocumentRole.ExternalUrl);

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

        // The probe reports on a completion that has already happened, so a navigation started while it was
        // in flight owns the document now and this verdict is about a page that has been left.
        if (ViewModel.IsNavigating
            || !string.Equals(ViewModel.CurrentUrl, probedUrl, StringComparison.Ordinal))
        {
            return;
        }

        var surface = Surface;
        Diagnostics.LogProbe(surface, probedUrl, probe);

        if (probe.IsEmpty && surface.TreatEmptyDocumentAsFailure)
        {
            // Reported as the failure it is, so the document shows the load-failed placeholder and its reload
            // rather than a blank page the user cannot tell from a slow one.
            ViewModel.NotifyNavigationCompleted(false);
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

        // Reset the tool bridge's content-ready gate so webview_* tool calls block
        // until the new navigation completes. Cross-origin navigations (e.g. an
        // attacker-controlled redirect from project content) reset support here too.
        if (Options.Role == WebViewDocumentRole.HtmlViewer)
        {
            _toolBridge?.NotifyContentLoading(FileResource);
        }

        ViewModel.NotifyNavigationStarted();
        if (!string.IsNullOrEmpty(args.Uri))
        {
            ViewModel.CurrentUrl = args.Uri;
            _pendingNavigationUrl = args.Uri;
        }
    }

    private void UpdateNavigationState()
    {
        if (_webView is null)
        {
            return;
        }

        ViewModel.CanGoBack = _webView.CanGoBack;
        ViewModel.CanGoForward = _webView.CanGoForward;

        // Source names the last committed navigation, so while one is in flight it still reports the page
        // being left. With nothing pending it is the only signal for a same-document navigation, which
        // raises no navigation event.
        var committedUrl = _webView.CoreWebView2?.Source;
        if (string.IsNullOrEmpty(committedUrl))
        {
            return;
        }

        if (_pendingNavigationUrl.Length > 0
            && committedUrl != _pendingNavigationUrl)
        {
            return;
        }

        _pendingNavigationUrl = string.Empty;

        // CoreWebView2.Source rather than WebView2.Source, which reports the address percent-encoded where
        // this reports it as the page shows it.
        ViewModel.CurrentUrl = committedUrl;
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
            // Abandon the edit: restore the address the page is actually showing and return to the content.
            SyncAddressText();

            // The settings keep the document area until the user leaves them, so there is no page waiting
            // for the keyboard while they are showing.
            if (ViewModel.IsPageOnScreen)
            {
                GiveFocusToWebContent();
            }

            e.Handled = true;
        }
    }

    // A document with neither an address nor a URL bar to type one into has no way in, so it opens on the
    // settings whatever state it was saved in. Home is the section holding the URL, and a restored section
    // would otherwise land the user somewhere that cannot help.
    private void OpenSettingsIfNoWayToNavigate()
    {
        if (Options.Role != WebViewDocumentRole.ExternalUrl
            || !string.IsNullOrWhiteSpace(ViewModel.SourceUrl)
            || ViewModel.ShowUrlBar)
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
        if (Options.Role != WebViewDocumentRole.ExternalUrl
            || ViewModel.HasPage)
        {
            return;
        }

        TryNavigate();
    }

    private void SettingsSurface_ReturnToPageRequested(object? sender, EventArgs e)
    {
        ViewModel.CloseSettings();

        // The button that asked has just collapsed with the settings, so the keyboard has nowhere to go.
        GiveFocusToWebContent();
    }

    // Gives the document area to whichever of the page, the settings and the placeholder belongs there.
    // The WebView is collapsed rather than covered by either of the other two: a hosted web view is a
    // native view above the canvas they are drawn on, so while it is shown it takes mouse input meant for
    // them, and the cursor over it answers to the page.
    private void ApplyContentLayout()
    {
        var showSettings = ViewModel.IsSettingsVisible;
        if (showSettings)
        {
            SettingsSurface.Initialize(ViewModel, _settingsSectionKey);

            // Find applies to the page, which is no longer on screen. Closing the bar hands the keyboard
            // back to the page, so only do it when the bar is actually showing.
            if (FindBar.Visibility == Visibility.Visible)
            {
                FindBar.Close();
            }
        }

        SettingsSurface.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        ContentPlaceholder.Visibility = ViewModel.IsPlaceholderVisible ? Visibility.Visible : Visibility.Collapsed;
        AppWebViewContainer.Visibility = ViewModel.IsPageOnScreen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DownloadIndicatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsDownloadSucceeded)
        {
            ViewModel.RevealLastDownload();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WebViewDocumentViewModel.IsNavigating))
        {
            UpdateReloadOrStopTooltip();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.DownloadStatus))
        {
            UpdateDownloadIndicatorTooltip();
        }
        else if (e.PropertyName == nameof(WebViewDocumentViewModel.IsSettingsVisible))
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

    private void UpdateDownloadIndicatorTooltip()
    {
        string? key = ViewModel.DownloadStatus switch
        {
            WebViewDownloadStatus.InProgress => "WebView_UrlBar_DownloadInProgressTooltip",
            WebViewDownloadStatus.Succeeded => "WebView_UrlBar_DownloadSucceededTooltip",
            WebViewDownloadStatus.Failed => "WebView_UrlBar_DownloadFailedTooltip",
            _ => null,
        };

        if (key is null)
        {
            ToolTipService.SetToolTip(DownloadIndicatorButton, null);
            return;
        }

        string tooltip = _stringLocalizer.GetString(key);
        ToolTipService.SetToolTip(DownloadIndicatorButton, tooltip);
    }

    // Holds the settled indicator visible briefly, then fades it out.
    private void ScheduleDownloadIndicatorDismiss()
    {
        if (_downloadIndicatorDismissTimer is null)
        {
            _downloadIndicatorDismissTimer = new DispatcherTimer
            {
                Interval = DownloadIndicatorDismissDelay,
            };
            _downloadIndicatorDismissTimer.Tick += DownloadIndicatorDismissTimer_Tick;
        }

        _downloadIndicatorDismissTimer.Stop();
        _downloadIndicatorDismissTimer.Start();
    }

    private void DownloadIndicatorDismissTimer_Tick(object? sender, object e)
    {
        _downloadIndicatorDismissTimer?.Stop();

        // A new download may have started during the dismiss delay; leave its
        // in-progress indicator alone.
        if (ViewModel.IsDownloadInProgress)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
        };
        Storyboard.SetTarget(animation, DownloadIndicatorButton);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            ViewModel.ClearDownloadIndicator();
            DownloadIndicatorButton.Opacity = 1.0;
        };
        storyboard.Begin();
    }

    private async void CoreWebView2_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        // WebView2 reads args mutations only after the handler completes the
        // deferral. Without the deferral, an await mid-handler would let the
        // runtime proceed with the original args before our overrides land.
        var deferral = args.GetDeferral();
        try
        {
            // The URL bar's download indicator is the download UI, so suppress
            // WebView2's own download flyout.
            args.Handled = true;

            var downloadPath = args.ResultFilePath;
            if (string.IsNullOrEmpty(downloadPath))
            {
                args.Cancel = true;
                return;
            }

            var filename = Path.GetFileName(downloadPath);

            // Downloads land under project:downloads/ so the project root stays
            // uncluttered when a session produces multiple downloads.
            var requestedDestResource = new ResourceKey($"{ProjectConstants.DownloadsFolder}/{filename}");
            var resolveResult = ResourceRegistry.ResolveResourcePath(requestedDestResource);
            if (resolveResult.IsFailure)
            {
                args.Cancel = true;
                return;
            }
            var requestedPath = resolveResult.Value;
            var getResult = await GetUniquePathAsync(requestedPath);
            if (getResult.IsFailure)
            {
                args.Cancel = true;
                return;
            }
            var savePath = getResult.Value;

            var getResourceResult = ResourceRegistry.GetResourceKey(savePath);
            if (getResourceResult.IsFailure)
            {
                args.Cancel = true;
                return;
            }
            var saveResourceKey = getResourceResult.Value;

            // Probe the destination before staging so policy denials surface to
            // the user up front instead of after the transfer completes.
            var probeResult = await ResourceFileSystem.GetInfoAsync(saveResourceKey);
            if (probeResult.IsFailure)
            {
                args.Cancel = true;
                _logger.LogError($"Download blocked: {probeResult.FirstErrorMessage}");

                var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
                var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();
                var projectService = _serviceProvider.GetRequiredService<IProjectService>();
                var projectFileName = Path.GetFileName(projectService.CurrentProject?.ProjectFilePath ?? string.Empty);

                var title = stringLocalizer.GetString("WebView_DownloadBlocked_Title");
                var message = stringLocalizer.GetString(
                    "WebView_DownloadBlocked_Message",
                    filename,
                    projectFileName);
                await dialogService.ShowAlertDialogAsync(title, message);
                return;
            }

            // Stage the download under the project's temp: root so the staging
            // location lives alongside the rest of the workspace's scratch space
            // and the wipe-on-load policy bounds orphan accumulation.
            var extension = Path.GetExtension(filename);
            var randomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
            var downloadResource = new ResourceKey($"temp:{ProjectConstants.DownloadsFolder}/{randomName}{extension}");
            var resolveTempResult = ResourceRegistry.ResolveResourcePath(downloadResource);
            if (resolveTempResult.IsFailure)
            {
                args.Cancel = true;
                return;
            }
            var tempPath = resolveTempResult.Value;
            args.ResultFilePath = tempPath;

            ViewModel.BeginDownload();

            args.DownloadOperation.StateChanged += async (s, e) =>
            {
                // Async-void event handler: any escaping exception ends up on the
                // SynchronizationContext's unhandled-exception channel, so wrap
                // the body so a WebView-side failure can't crash the host.
                try
                {
                    if (s.State == CoreWebView2DownloadState.Completed)
                    {
                        var importResult = await _commandService.ExecuteAsync<ICreateResourceCommand>(command =>
                        {
                            command.ResourceType = ResourceType.File;
                            command.SourcePath = tempPath;
                            command.DestResource = saveResourceKey;
                        });

                        // The import copies bytes (no cross-root move from temp:
                        // to project:), so the staging copy is always redundant
                        // afterwards. Delete it on failure too, or it leaks.
                        await ResourceFileSystem.DeleteAsync(downloadResource);

                        if (importResult.IsFailure)
                        {
                            ViewModel.FailDownload();
                            _logger.LogError(
                                $"Failed to import downloaded file to '{saveResourceKey}'. {importResult.DiagnosticReport}");
                        }
                        else
                        {
                            ViewModel.CompleteDownload(saveResourceKey);
                        }

                        ScheduleDownloadIndicatorDismiss();
                    }
                    else if (s.State == CoreWebView2DownloadState.Interrupted)
                    {
                        await ResourceFileSystem.DeleteAsync(downloadResource);

                        ViewModel.FailDownload();
                        ScheduleDownloadIndicatorDismiss();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Download state change handler failed");
                }
            };
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Returns a path that doesn't collide with an existing file or folder by
    // appending " (N)" before any extension.
    private static async Task<Result<string>> GetUniquePathAsync(string path)
    {
        try
        {
            path = Path.GetFullPath(path);

            string directoryPath = Path.GetDirectoryName(path)!;
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string uniqueName = Path.GetFileName(path);
            int count = 1;

            var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();

            while (true)
            {
                var candidatePath = Path.Combine(directoryPath, uniqueName);
                var infoResult = await fileSystem.GetInfoAsync(candidatePath);
                bool exists = infoResult.IsSuccess
                    && infoResult.Value.Kind != StorageItemKind.NotFound;
                if (!exists)
                {
                    break;
                }

                if (!string.IsNullOrEmpty(extension))
                {
                    uniqueName = $"{nameWithoutExtension} ({count}){extension}";
                }
                else
                {
                    uniqueName = $"{nameWithoutExtension} ({count})";
                }
                count++;
            }

            return Path.Combine(directoryPath, uniqueName);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to generate a unique path: {path}")
                .WithException(ex);
        }
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var previousResource = FileResource;

        var setResult = await base.SetFileResource(fileResource);
        if (setResult.IsFailure)
        {
            return setResult;
        }

        // A rename reuses this view, so the bridge entry has to follow the resource. Left on the
        // old key, every webview_* call for the renamed document finds no registration and the
        // stale entry survives until the workspace closes.
        _toolBridge?.Rekey(previousResource, FileResource);

        return setResult;
    }

    public override async Task<Result> LoadContent()
    {
        // Push the role onto the view model so NavigateUrl knows which URL to compute.
        ViewModel.Role = Options.Role;

        var loadResult = await ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        OpenSettingsIfNoWayToNavigate();

        // Runs after the view model so NavigateUrl is resolved by the time initialization navigates.
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
        // layout store would then write over good saved state. The HTML viewer has no settings at all.
        if (Options.Role != WebViewDocumentRole.ExternalUrl ||
            _webView is null)
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

    public override async Task RestoreEditorStateAsync(string state)
    {
        await Task.CompletedTask;

        if (Options.Role != WebViewDocumentRole.ExternalUrl)
        {
            return;
        }

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
    public bool CanRestoreChrome => Options.Role == WebViewDocumentRole.ExternalUrl && !ViewModel.ShowUrlBar;

    public string RestoreChromeMenuTextKey => "WebView_ShowUrlBar";

    public void RestoreChrome()
    {
        ViewModel.ShowUrlBar = true;
    }

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.OpenBrowser(url);
        }
    }

    public override IEditTarget EditTarget { get; } = new DisabledEditTarget();

    public override void FocusDocument()
    {
        if (ViewModel.IsSettingsVisible)
        {
            // The page is collapsed behind the settings, and native focus on macOS would land on a hidden
            // web view that no keystroke could ever leave.
            SettingsSurface.Focus(FocusState.Programmatic);
            return;
        }

        // A tab click focuses the web content (native first responder on macOS, where no managed GotFocus
        // follows). The registry gives it focus and reports it, releasing the previously focused surface.
        GiveFocusToWebContent();
    }

    // Hands keyboard focus to the page through the registry, which applies native focus on macOS and reports
    // the focus so the panel focus follows. Used by every path that finishes with the chrome and returns the
    // user to the content.
    private void GiveFocusToWebContent()
    {
        if (_webView is null)
        {
            _logger.LogWarning("Cannot focus the page before its WebView is created");
            return;
        }

        _webViewFocusRegistry.GrantFocus(_webView);
    }

    private void ReleaseFocus()
    {
        _ = _host?.NotifyReleaseFocusAsync();
    }

    // Native focus gives the page the keyboard but leaves no element inside it focused, so a page that was
    // released when its tab lost focus needs the DOM focus handed back.
    private async Task GrantDomFocusAsync()
    {
        var host = _host;
        if (host is null)
        {
            return;
        }

        await host.NotifyGrantFocusAsync();
    }

    // True when the host find bar can drive this document: the page is the thing on screen, the WebView is
    // live, and its backend has no find UI of its own (the Windows Chromium heads do, so they report false
    // and keep their built-in bar).
    public bool CanFind => ViewModel.IsPageOnScreen
        && !_webViewAdapter.ProvidesBuiltInFind
        && _webView?.CoreWebView2 is not null;

    public bool TryBeginFind()
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
        // Hand focus back to the page so subsequent keystrokes reach the content, not the hidden find bar.
        GiveFocusToWebContent();
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
        _downloadIndicatorDismissTimer?.Stop();

        TeardownWebViewState();

        await base.PrepareToClose();
    }
}
