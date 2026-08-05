using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace Celbridge.WebView.Views;

/// <summary>
/// Hosts an arbitrary user URL from a .webview document, or a project-served
/// HTML page from a .html / .htm document. The two roles share a single WebView2
/// lifecycle and differ only in URL source, navigation policy, and chrome: the
/// external-URL role presents a browser-style URL bar above the page.
/// </summary>
public sealed partial class WebViewDocumentView : DocumentView, IHostInput, IFindableDocument, IWebViewFindTarget
{
    // How long the settled download indicator stays visible before fading out.
    private static readonly TimeSpan DownloadIndicatorDismissDelay = TimeSpan.FromSeconds(10);

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

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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

            // The external-URL role injects no script, so on macOS the registry's native click monitor
            // supplies the click-focus signal for content that raises no DOM focus event. This surface
            // hosts no edit target.
            RegisterWebSurfaceFocus(_webView, editTarget: null, ReleaseFocus);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = _webViewService.IsDevToolsFeatureEnabled();

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

                _hostChannel = new WebViewHostChannel(_webView.CoreWebView2);
                _host = new CelbridgeHost(_hostChannel);
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

        ViewModel.IsNavigating = false;
        UpdateNavigationState();
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Reset the tool bridge's content-ready gate so webview_* tool calls block
        // until the new navigation completes. Cross-origin navigations (e.g. an
        // attacker-controlled redirect from project content) reset support here too.
        if (Options.Role == WebViewDocumentRole.HtmlViewer)
        {
            _toolBridge?.NotifyContentLoading(FileResource);
        }

        ViewModel.IsNavigating = true;
        if (!string.IsNullOrEmpty(args.Uri))
        {
            ViewModel.CurrentUrl = args.Uri;
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
        ViewModel.CurrentUrl = _webView.Source?.AbsoluteUri ?? string.Empty;
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
                // Unlike the other navigation commands, Stop has no equivalent on the
                // WebView2 control, so it drops to CoreWebView2.
                coreWebView2.Stop();
            }
            else
            {
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
            Navigate(homeUrl);
        }
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenBrowser(ViewModel.CurrentUrl);
    }

    private void AddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (ViewModel.TryNormalizeUserUrl(AddressTextBox.Text, out var url))
            {
                Navigate(url);

                // Move focus away from the text box so the user gets immediate
                // visual feedback that the Enter press registered.
                Focus(FocusState.Programmatic);
            }

            e.Handled = true;
        }
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

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.OpenBrowser(url);
        }
    }

    public override void FocusDocument()
    {
        // A tab click focuses the web content (native first responder on macOS, where no managed GotFocus
        // follows). The registry gives it focus and reports it, releasing the previously focused surface.
        if (_webView is not null)
        {
            _webViewFocusRegistry.GrantFocus(_webView);
        }
    }

    private void ReleaseFocus()
    {
        _ = _host?.NotifyReleaseFocusAsync();
    }

    // True when the host find bar can drive this document: the WebView is live and its backend has no find UI
    // of its own (the Windows Chromium heads do, so they report false and keep their built-in bar).
    public bool CanFind => !_webViewAdapter.ProvidesBuiltInFind && _webView?.CoreWebView2 is not null;

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
        // Routing through the registry grant reports the focus too, so the panel focus reflects the page
        // again after the find bar closes.
        if (_webView is not null)
        {
            _webViewFocusRegistry.GrantFocus(_webView);
        }
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
        _downloadIndicatorDismissTimer?.Stop();

        TeardownWebViewState();

        await base.PrepareToClose();
    }
}
