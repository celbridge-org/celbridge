using System.Text.Json;
using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.FileSystem;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Server;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for contribution-based editors, hosted via a WebView2.
/// </summary>
public sealed partial class ContributionDocumentView : DocumentView, IHostInput, IHostContext, IEditTarget
{
    private const int SaveRequestTimeoutSeconds = 30;
    private const int ReloadStateWaitSeconds = 5;

    // Editor-state capture/restore is best-effort (a user convenience, not data). Bound the wait so an
    // editor that never responds to the host->editor RPC cannot stall document close, which would jam
    // the serial command queue. Kept under the command watchdog's 5s threshold.
    private const int EditorStateRequestTimeoutSeconds = 3;

    // Bound the wait for the about:blank unload navigation on close so an unresponsive page cannot stall
    // document close (which would jam the serial command queue). Kept under the command watchdog's threshold.
    private const int BlankNavigationTimeoutSeconds = 2;

    private readonly ILogger<ContributionDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;
    private readonly IFocusService _focusService;

    // Latest edit availability reported by the editor over the bridge; drives CanPerformEdit.
    private EditAvailability _editAvailability = EditAvailability.None;

    private readonly ContributionDocumentViewModel _viewModel;

    private ContributionDocumentHandler? _documentHandler;
    private ContributionToolsHandler? _toolsHandler;
    private IDisposable? _appStateConnection;

    // This view's own state store (writability), mirrored to its WebView over the viewState channel.
    private IStateStore? _viewState;
    private IDisposable? _viewStateConnection;

    // JSON-RPC infrastructure. Teardown detaches the WebView2 channel or disposes the deferred
    // WebSocket channel, depending on the transport the host channel factory selected.
    private Action? _hostChannelTeardown;

    // WebView tool bridge registration tracking. Only set when the package allows the
    // webview_* tools and the registration has succeeded; the field doubles as a guard
    // for unregistration.
    private IDocumentWebViewToolBridge? _toolBridge;
    private ResourceKey _toolBridgeRegisteredResource;

    // Deferred editor state for views where the WebView initializes asynchronously.
    // RestoreEditorStateAsync stores state here when the editor isn't ready yet,
    // and SetContentLoaded applies it once the JS client signals readiness.
    private string? _pendingEditorStateJson;
    private bool _isContentLoaded;

    // Set while the view is closing so the navigation gate lets the about:blank unload navigation through.
    private bool _isClosing;

    // Save tracking state for async save coordination with WebView
    private bool _isSaveInProgress;
    private bool _hasPendingSave;

    // Reload coalescing: at most one external-reload runs at a time. FileSystemWatcher
    // often emits duplicate Changed events for a single logical write, and the
    // editor host cannot tolerate concurrent NotifyExternalChangeAsync calls.
    // Requests arriving while a reload is in flight collapse into a single
    // follow-up pass.
    private readonly object _reloadLock = new();
    private bool _isReloadInProgress;
    private bool _hasPendingReload;

    // Completed by InitContributionViewAsync with the init outcome. LoadContent
    // triggers the init on first call and awaits this TCS so the open-document
    // flow returns only when the WebView and host are ready for RPCs.
    private TaskCompletionSource<Result>? _initTcs;

    /// <summary>
    /// The WebView2 control acquired from the factory.
    /// </summary>
    private WebView2? WebView { get; set; }

    /// <summary>
    /// The Celbridge host for JSON-RPC communication with the WebView.
    /// </summary>
    private CelbridgeHost? Host { get; set; }

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The document contribution that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public CustomDocumentEditorContribution? Contribution { get; set; }

    public ContributionDocumentView(
        IServiceProvider serviceProvider,
        ILogger<ContributionDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWebViewFactory webViewFactory,
        IWebViewService webViewService)
    {
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _userInterfaceService = userInterfaceService;
        _serviceProvider = serviceProvider;
        _webViewFactory = webViewFactory;
        _webViewService = webViewService;
        _focusService = ServiceLocator.AcquireService<IFocusService>();

        _viewModel = serviceProvider.GetRequiredService<ContributionDocumentViewModel>();

        this.InitializeComponent();

        _viewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        if (Host is null || _documentHandler is null)
        {
            _logger.LogDebug("Save skipped - Host not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        var saveResultTcs = new TaskCompletionSource<Result>();
        _documentHandler.SaveResultTcs = saveResultTcs;

        await Host.NotifyRequestSaveAsync();

        var timeout = TimeSpan.FromSeconds(SaveRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(saveResultTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _documentHandler.SaveResultTcs = null;
            CompleteSave();

            var errorMessage = $"Contribution editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"File: {_viewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await saveResultTcs.Task;
        _documentHandler.SaveResultTcs = null;

        return result;
    }

    private bool TryBeginSave()
    {
        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            return false;
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;
        return true;
    }

    private bool CompleteSave()
    {
        _isSaveInProgress = false;

        if (_hasPendingSave)
        {
            _hasPendingSave = false;
            return true;
        }

        return false;
    }

    private IContributionEditorLoader ResolveContributionEditorLoader(PackageInfo package)
    {
        // The loopback default is registered first and matches every package, so it is the fallback; a
        // module's custom loader is registered later and wins as the last matching loader.
        IContributionEditorLoader? selected = null;
        foreach (var candidate in _serviceProvider.GetServices<IContributionEditorLoader>())
        {
            if (candidate.CanLoad(package))
            {
                selected = candidate;
            }
        }

        Guard.IsNotNull(selected);

        return selected;
    }

    private async Task InitContributionViewAsync()
    {
        if (Contribution is null)
        {
            var error = "Cannot initialize contribution view: Contribution is not set";
            _logger.LogError(error);
            _initTcs!.TrySetResult(Result.Fail(error));
            return;
        }

        _viewModel.Contribution = Contribution;

        var editorLoader = ResolveContributionEditorLoader(Contribution.Package);

        // In-place vs. pooled is a platform decision, not a loader one: re-parenting a pooled control resets
        // the WebKit context on the Skia heads, so those create the WebView in place; Windows reuses the
        // pre-warmed pool, where re-parenting is harmless.
        if (OperatingSystem.IsWindows())
        {
            await InitPooledWebViewAsync(editorLoader);
        }
        else
        {
            await InitInPlaceWebViewAsync(editorLoader);
        }
    }

    // Windows: the pre-warmed pool hands back a control with CoreWebView2 already live, so the host is
    // configured immediately and init is signalled only once the editor's navigation has started.
    private async Task InitPooledWebViewAsync(IContributionEditorLoader editorLoader)
    {
        Guard.IsNotNull(Contribution);

        try
        {
            WebView = await _webViewFactory.AcquireAsync();
            ContributionWebViewContainer.Children.Add(WebView);

            await ConfigureWebViewHostAsync(editorLoader);

            _initTcs!.TrySetResult(Result.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize contribution view: {Contribution.Package.Name}");
            TeardownWebViewState();
            var failure = Result.Fail($"Failed to initialize contribution view: {Contribution.Package.Name}")
                .WithException(ex);
            _initTcs!.TrySetResult(failure);
        }
    }

    // Skia heads: create the control in place and never re-parent it, so its WebKit context stays intact.
    // EnsureCoreWebView2Async completes only once the control is window-rooted (Loaded), which is after
    // LoadContent returns -- so init is signalled up front and the host is configured once the control loads.
    // A failure past that point is logged and torn down rather than surfaced, since init has already completed.
    private async Task InitInPlaceWebViewAsync(IContributionEditorLoader editorLoader)
    {
        Guard.IsNotNull(Contribution);

        WebView = new WebView2
        {
            DefaultBackgroundColor = Microsoft.UI.Colors.Transparent
        };
        ContributionWebViewContainer.Children.Add(WebView);
        _initTcs!.TrySetResult(Result.Ok());

        try
        {
            await WaitForLoadedAsync(WebView);
            await WebView.EnsureCoreWebView2Async();
            await ConfigureWebViewHostAsync(editorLoader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize in-place contribution view: {Contribution.Package.Name}");
            TeardownWebViewState();
        }
    }

    // Completes once the element is loaded into the visual tree, or immediately if it already is.
    private static Task WaitForLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded)
        {
            return Task.CompletedTask;
        }

        var loadedCompletionSource = new TaskCompletionSource();
        RoutedEventHandler? onLoaded = null;
        onLoaded = (sender, args) =>
        {
            element.Loaded -= onLoaded;
            loadedCompletionSource.TrySetResult();
        };
        element.Loaded += onLoaded;

        return loadedCompletionSource.Task;
    }

    // Configures a live WebView (CoreWebView2 ready): host channel, RPC targets, tool bridge, navigation gate,
    // and the editor load. Shared by the pooled and in-place init paths.
    private async Task ConfigureWebViewHostAsync(IContributionEditorLoader editorLoader)
    {
        Guard.IsNotNull(Contribution);
        Guard.IsNotNull(WebView);

        // DevTools is off when the hosting package blocks it (sensitive material)
        // or when the user has not enabled the WebViewDevTools feature flag.
        var devToolsBlocked = Contribution.Package.DevToolsBlocked;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled =
            !devToolsBlocked && _webViewService.IsDevToolsFeatureEnabled();

        WebView.GotFocus -= WebView_GotFocus;
        WebView.GotFocus += WebView_GotFocus;

        // Every contribution editor's assets (its lib, the shared client) are served from the loopback
        // file server, so register this package's folder there. The resolved loader decides where the
        // entry page itself loads from.
        var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
        var packageUrlName = Contribution.Package.Name.Replace('.', '-');

        fileServer.RegisterPackageFolder(packageUrlName, Contribution.Package.PackageFolder);

        WarnOnEmptyPackageSecrets();

        // Inject the in-page tool bridge shim for the webview_* MCP tool namespace.
        // Skipped when the package opts out via DevToolsBlocked (sensitive material)
        // so that no tool surface is exposed for those packages.
        if (!devToolsBlocked)
        {
            await TryInjectToolBridgeShimAsync();
        }

        // Block all new window requests
        WebView.CoreWebView2.NewWindowRequested += (s, args) =>
        {
            args.Handled = true;
        };

        WebView.CoreWebView2.ProcessFailed += (s, args) =>
        {
            _logger.LogError(
                "WebView ProcessFailed: Kind={Kind}, Reason={Reason}, ExitCode={ExitCode}",
                args.ProcessFailedKind, args.Reason, args.ExitCode);
        };

        // Wire up the JSON-RPC host channel. The loader's declared transport selects between the loopback
        // WebSocket (the page derives the socket URL from its own origin plus a connection token) and the
        // WebView2 message channel (for a page that is not same-origin with the loopback server).
        var useWebSocketChannel = editorLoader.GetTransport(Contribution.Package) == HostChannelTransport.LoopbackWebSocket;
        var hostChannelBroker = _serviceProvider.GetRequiredService<IHostChannelBroker>();
        var hostChannelSetup = HostChannelFactory.Create(WebView.CoreWebView2, useWebSocketChannel, hostChannelBroker);
        _hostChannelTeardown = hostChannelSetup.Teardown;
        var connectionToken = hostChannelSetup.ConnectionToken;
        Host = new CelbridgeHost(hostChannelSetup.Channel);

        Host.AddLocalRpcTarget<IHostInput>(this);
        Host.AddLocalRpcTarget<IHostContext>(this);

        _documentHandler = new ContributionDocumentHandler(
            _viewModel,
            _logger,
            CreateDocumentMetadata,    // Callback to construct document metadata on demand
            CompleteSave);             // Callback to update state when saving has completed

        _documentHandler.ContentLoaded += SetContentLoaded;

        var dialogHandler = new ContributionDialogHandler(
            _dialogService,
            _stringLocalizer,
            _viewModel);

        Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
        Host.AddLocalRpcTarget<IHostDialog>(dialogHandler);

        var mcpToolBridge = _serviceProvider.GetService<IMcpToolBridge>();
        if (mcpToolBridge is not null)
        {
            _toolsHandler = new ContributionToolsHandler(mcpToolBridge, Contribution.Package.PermittedTools);
            Host.AddLocalRpcTarget<ContributionToolsHandler>(_toolsHandler);
        }

        Host.StartListening();

        // Registering pushes the current snapshot, so it must run after StartListening. Seed writability
        // before registering so the connect push carries it.
        var stateService = _serviceProvider.GetRequiredService<IWebViewStateService>();
        var capturedHost = Host;
        _appStateConnection = stateService.AppState.RegisterConnection(
            snapshot => capturedHost.Rpc.NotifyWithParameterObjectAsync(StateRpcMethods.AppStateChanged, snapshot));

        _viewState = stateService.CreateViewState();
        _viewState.SetValue("writable", WritableState.ToString());
        _viewStateConnection = _viewState.RegisterConnection(
            snapshot => capturedHost.Rpc.NotifyWithParameterObjectAsync(StateRpcMethods.ViewStateChanged, snapshot));

        // Register with the WebView tool bridge so the webview_* MCP tools can
        // target this WebView by resource key. Mirrors the shim injection guard.
        if (!devToolsBlocked)
        {
            TryRegisterWithToolBridge();
        }

        var entryPoint = Contribution.EntryPoint;
        var serverPort = _serviceProvider.GetRequiredService<IServerService>().Port;
        var loadRequest = new ContributionEditorLoadRequest(
            WebView!,
            Contribution.Package,
            packageUrlName,
            entryPoint,
            connectionToken,
            serverPort);

        // Block all navigations except the editor's own origin. Each allowed navigation also resets the
        // tool bridge's content-ready gate so webview_* tool calls block until the editor signals
        // readiness post-navigation.
        var allowedNavigationPrefix = editorLoader.GetAllowedNavigationOrigin(loadRequest);
        WebView!.NavigationStarting += (s, args) =>
        {
            var uri = args.Uri;
            if (string.IsNullOrEmpty(uri))
            {
                return;
            }

            // Let the about:blank unload navigation through on close (see UnloadWebViewPageAsync).
            if (_isClosing)
            {
                return;
            }

            if (uri.StartsWith(allowedNavigationPrefix))
            {
                _toolBridge?.NotifyContentLoading(_toolBridgeRegisteredResource);
                return;
            }

            args.Cancel = true;
        };

        await editorLoader.LoadAsync(loadRequest);
    }

    // On the Skia heads WebView2.Close() is an unimplemented no-op and the native WKWebView is not destroyed
    // on teardown, so a page left loaded keeps running its scripts -- and any audio -- after the document is
    // closed. Navigate to about:blank first to discard the page's script realm before the control is removed.
    // Windows disposes the WebView2 on Close(), so this is only needed on the Skia heads.
    private async Task UnloadWebViewPageAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var coreWebView2 = WebView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            return;
        }

        _isClosing = true;

        var unloadCompletionSource = new TaskCompletionSource();
        void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            unloadCompletionSource.TrySetResult();
        }

        coreWebView2.NavigationCompleted += OnNavigationCompleted;
        try
        {
            coreWebView2.Navigate("about:blank");

            // Wait for the unload to commit so the discarded page's scripts stop before teardown, but bound it
            // so an unresponsive page cannot stall close.
            var timeout = Task.Delay(TimeSpan.FromSeconds(BlankNavigationTimeoutSeconds));
            await Task.WhenAny(unloadCompletionSource.Task, timeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unload the WebView page on close");
        }
        finally
        {
            coreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    /// <summary>
    /// Tears down the WebView, host channel, and associated handlers. Safe to call
    /// multiple times and from partially initialized states.
    /// </summary>
    private void TeardownWebViewState()
    {
        if (_toolBridge is not null)
        {
            _toolBridge.Unregister(_toolBridgeRegisteredResource);
            _toolBridge = null;
            _toolBridgeRegisteredResource = ResourceKey.Empty;
        }

        if (_documentHandler is not null)
        {
            _documentHandler.ContentLoaded -= SetContentLoaded;
        }

        if (WebView is not null)
        {
            WebView.GotFocus -= WebView_GotFocus;
            ContributionWebViewContainer.Children.Remove(WebView);
            WebView.Close();
            WebView = null;
        }

        _appStateConnection?.Dispose();
        _viewStateConnection?.Dispose();
        Host?.Dispose();
        _hostChannelTeardown?.Invoke();

        _appStateConnection = null;
        _viewStateConnection = null;
        _viewState = null;
        Host = null;
        _hostChannelTeardown = null;
    }

    private async Task TryInjectToolBridgeShimAsync()
    {
        var coreWebView2 = WebView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            return;
        }

        var toolBridge = _serviceProvider.GetService<IDocumentWebViewToolBridge>();
        if (toolBridge is null)
        {
            return;
        }

        // Install the tool bridge shim as a document-start script so it wraps console/fetch before page
        // scripts run -- required for get_console / get_network capture. The Skia heads also re-deliver it
        // per navigation through OnNavigationCompleted_ReinjectShim.
        try
        {
            var script = toolBridge.GetShimScript();
            await coreWebView2.InstallDocumentStartScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install the document-start WebView tool bridge shim");
        }

        coreWebView2.NavigationCompleted += OnNavigationCompleted_ReinjectShim;
    }

    private async void OnNavigationCompleted_ReinjectShim(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        // async void event handler: swallow exceptions to protect the process. The bridge caches the shim
        // after the first read, and the shim is idempotent. Re-injection is a no-op on Windows, where the
        // document-start script persists across navigations.
        if (_toolBridge is null)
        {
            return;
        }

        try
        {
            var script = _toolBridge.GetShimScript();
            await sender.ReinjectDocumentStartScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-inject the WebView tool bridge shim");
        }
    }

    private void TryRegisterWithToolBridge()
    {
        var webView = WebView;
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

        toolBridge.RegisterWebView2(resource, webView);

        _toolBridge = toolBridge;
        _toolBridgeRegisteredResource = resource;
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    protected override void OnWritableStateChanged()
    {
        // The store may not exist yet (SetWritableState runs before LoadContent); the seed at registration
        // captures the current value, so an early change before connect is not lost.
        _viewState?.SetValue("writable", WritableState.ToString());
    }

    private DocumentMetadata CreateDocumentMetadata()
    {
        var locale = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var metaData = new DocumentMetadata(
            DocumentViewModel.FilePath,
            DocumentViewModel.FileResource.ToString(),
            Path.GetFileName(DocumentViewModel.FilePath),
            locale);

        return metaData;
    }

    private async void SetContentLoaded(ContentLoadedReason reason = ContentLoadedReason.Initial)
    {
        // Tool bridge readiness fires for every reason — initial load, external reload,
        // and programmatic webview_reload — so gated webview_* calls unblock as soon
        // as the editor signals it has reinitialised post-navigation.
        _toolBridge?.NotifyContentReady(_toolBridgeRegisteredResource);

        if (reason != ContentLoadedReason.Initial)
        {
            return;
        }

        _isContentLoaded = true;

        if (_pendingEditorStateJson is null)
        {
            return;
        }

        var state = _pendingEditorStateJson;
        _pendingEditorStateJson = null;

        try
        {
            await RestoreEditorStateAsync(state);
        }
        catch (Exception ex)
        {
            // Editor state restoration is best-effort: a corrupt or incompatible state should
            // never tear down the process. Log and swallow to preserve the async void safety contract.
            _logger.LogError(ex, "Failed to restore editor state after content loaded");
        }
    }

    private async Task ReloadWithStatePreservationAsync()
    {
        if (Host is null || _documentHandler is null)
        {
            return;
        }

        // Resolve the workspace-scoped documents service at call time, then drain
        // any reload hint registered by the command that triggered this reload.
        var workspaceWrapper = _serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        var documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        var hint = documentsService.ConsumeReloadHint(_viewModel.FileResource);
        bool preserveViewState = hint == ReloadHint.PreserveViewState;

        string? savedState = null;
        try
        {
            savedState = await TrySaveEditorStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture editor state before external reload; continuing without preservation");
        }

        var reloadComplete = new TaskCompletionSource();
        void OnReloaded(ContentLoadedReason reason)
        {
            if (reason == ContentLoadedReason.ExternalReload)
            {
                reloadComplete.TrySetResult();
            }
        }
        _documentHandler.ContentLoaded += OnReloaded;

        try
        {
            await Host.NotifyExternalChangeAsync(preserveViewState);

            var completed = await Task.WhenAny(reloadComplete.Task, Task.Delay(TimeSpan.FromSeconds(ReloadStateWaitSeconds)));
            if (completed != reloadComplete.Task)
            {
                return;
            }

            if (!string.IsNullOrEmpty(savedState))
            {
                await RestoreEditorStateAsync(savedState);
            }
        }
        finally
        {
            _documentHandler.ContentLoaded -= OnReloaded;
        }
    }

    public override async Task<string?> TrySaveEditorStateAsync()
    {
        if (Host is null || !_isContentLoaded)
        {
            return null;
        }

        // Race the request against a hard timeout and abandon it on timeout. A CancellationToken does
        // not work here: StreamJsonRpc cancellation waits for the editor to acknowledge the cancel, and
        // the unresponsive editor is exactly the failure being guarded against. State capture is
        // best-effort, so abandoning the request and closing without state is the correct degradation.
        var requestStateTask = Host.RequestStateAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(EditorStateRequestTimeoutSeconds));
        var completedTask = await Task.WhenAny(requestStateTask, timeoutTask);

        if (completedTask != requestStateTask)
        {
            _logger.LogWarning("Editor did not return state within {Seconds}s; closing without preserving editor state.", EditorStateRequestTimeoutSeconds);
            ObserveAbandonedRequest(requestStateTask);
            return null;
        }

        try
        {
            return await requestStateTask;
        }
        catch (StreamJsonRpc.RemoteMethodNotFoundException)
        {
            // Editor did not register a document/requestState handler.
            return null;
        }
    }

    // Swallows the eventual fault of an abandoned host->editor request (it faults when the WebView is
    // torn down) so it does not surface as an unobserved task exception.
    private static void ObserveAbandonedRequest(Task task)
    {
        _ = task.ContinueWith(
            static abandonedTask => { _ = abandonedTask.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public override async Task RestoreEditorStateAsync(string state)
    {
        if (!_isContentLoaded)
        {
            _pendingEditorStateJson = state;
            return;
        }

        var restoreStateTask = Host!.RestoreStateAsync(state);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(EditorStateRequestTimeoutSeconds));
        var completedTask = await Task.WhenAny(restoreStateTask, timeoutTask);

        if (completedTask != restoreStateTask)
        {
            // Best-effort restore; an unresponsive editor should not stall the caller. Abandon and move on.
            _logger.LogWarning("Editor did not acknowledge restoreState within {Seconds}s; continuing.", EditorStateRequestTimeoutSeconds);
            ObserveAbandonedRequest(restoreStateTask);
            return;
        }

        try
        {
            await restoreStateTask;
        }
        catch (StreamJsonRpc.RemoteMethodNotFoundException)
        {
            // Editor did not register a document/restoreState handler.
        }
    }

    public void OnLinkClicked(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return;
        }

        if (Contribution is null)
        {
            return;
        }

        var resolveResult = _viewModel.ResolveLinkTarget(href);

        if (resolveResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve link: {href}");
            _ = ShowLinkErrorAsync(href);
            return;
        }

        var resourceKey = resolveResult.Value;

        if (resourceKey.IsEmpty)
        {
            OpenSystemBrowser(_commandService, href);
        }
        else
        {
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
            });
        }
    }

    private static void OpenSystemBrowser(ICommandService commandService, string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

    private async Task ShowLinkErrorAsync(string href)
    {
        var errorTitle = _stringLocalizer.GetString("Extension_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Extension_LinkError_Message", href);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
    }

    public override async Task<Result> LoadContent()
    {
        // LoadContent drives WebView initialization directly — not via the
        // XAML Loaded event. DocumentsService.CreateDocumentView calls
        // LoadContent before the view is added to the visual tree, so a
        // Loaded-driven init would not fire in time to navigate the WebView.
        // We wait for init (host ready, navigation started) but do not block
        // on the editor's own notifyContentLoaded signal. Some heavyweight
        // editors can take seconds to finish importingcontent. Forcing callers
        // to wait for that would make document open feel slow.
        if (_initTcs is null)
        {
            _initTcs = new TaskCompletionSource<Result>();
            _ = InitContributionViewAsync();
        }

        var initResult = await _initTcs.Task;
        if (initResult.IsFailure)
        {
            return initResult;
        }

        return Result.Ok();
    }

    public override async Task<Result> NavigateToLocation(string location)
    {
        if (Host is null ||
            string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        try
        {
            using var doc = JsonDocument.Parse(location);
            var root = doc.RootElement;

            var lineNumber = root.TryGetProperty("lineNumber", out var lineProp) ? lineProp.GetInt32() : 1;
            var column = root.TryGetProperty("column", out var colProp) ? colProp.GetInt32() : 1;
            var endLineNumber = root.TryGetProperty("endLineNumber", out var endLineProp) ? endLineProp.GetInt32() : 0;
            var endColumn = root.TryGetProperty("endColumn", out var endColProp) ? endColProp.GetInt32() : 0;

            await Host.Rpc.NotifyWithParameterObjectAsync(
                "editor/navigateToLocation",
                new { lineNumber, column, endLineNumber, endColumn });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: {location}")
                .WithException(ex);
        }
    }

    public override async Task PrepareToClose()
    {
        _isContentLoaded = false;
        _pendingEditorStateJson = null;

        _messengerService.UnregisterAll(this);

        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;

        _viewModel.Cleanup();

        await UnloadWebViewPageAsync();

        TeardownWebViewState();

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        // Set this document as the active document when the WebView2 receives focus
        var message = new DocumentViewFocusedMessage(FileResource);
        _messengerService.Send(message);

        _focusService.OnFocusReceived(WorkspacePanel.Documents, this, ReleaseFocus);
    }

    public void OnFocusReceived()
    {
        // The Skia head does not raise WebView.GotFocus for clicks inside the WebView, so the JS client
        // reports DOM focus over the bridge. Marshal to the UI thread and register this editor as the
        // active surface; on Windows this is redundant with WebView_GotFocus and harmless.
        DispatcherQueue.TryEnqueue(() =>
        {
            var message = new DocumentViewFocusedMessage(FileResource);
            _messengerService.Send(message);

            _focusService.OnFocusReceived(WorkspacePanel.Documents, this, ReleaseFocus);
        });
    }

    private void ReleaseFocus()
    {
        _ = Host?.NotifyReleaseFocusAsync();
    }

    public bool CanPerformEdit(EditIntent intent)
    {
        return _editAvailability.Allows(intent);
    }

    public void PerformEdit(EditIntent intent)
    {
        // The WebView's own JS clipboard write is blocked outside a user gesture on the Skia WKWebView,
        // so the clipboard verbs are host-mediated: the host moves text between Monaco and the native
        // clipboard. Select-all, undo, and redo touch no clipboard and run inside the editor.
        switch (intent)
        {
            case EditIntent.Copy:
                _ = CopyEditorSelectionAsync(deleteSelection: false);
                break;

            case EditIntent.Cut:
                _ = CopyEditorSelectionAsync(deleteSelection: true);
                break;

            case EditIntent.Paste:
                _ = PasteIntoEditorAsync();
                break;

            case EditIntent.SelectAll:
                _ = Host?.NotifyPerformEditAsync("selectAll");
                break;

            case EditIntent.Undo:
                _ = Host?.NotifyPerformEditAsync("undo");
                break;

            case EditIntent.Redo:
                _ = Host?.NotifyPerformEditAsync("redo");
                break;
        }
    }

    private async Task CopyEditorSelectionAsync(bool deleteSelection)
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        try
        {
            var selectedText = await host.Rpc.InvokeAsync<string?>("editor/getSelectedText");
            if (string.IsNullOrEmpty(selectedText))
            {
                return;
            }

            _commandService.Execute<ICopyTextToClipboardCommand>(command => command.Text = selectedText);

            if (deleteSelection)
            {
                await host.Rpc.NotifyWithParameterObjectAsync("editor/insertText", new { text = string.Empty });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy the editor selection to the clipboard");
        }
    }

    private async Task PasteIntoEditorAsync()
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        try
        {
            var dataPackageView = Clipboard.GetContent();
            if (!dataPackageView.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await dataPackageView.GetTextAsync();
            await host.Rpc.NotifyWithParameterObjectAsync("editor/insertText", new { text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste the clipboard into the editor");
        }
    }

    public void OnEditAvailabilityChanged(
        bool canCopy,
        bool canCut,
        bool canPaste,
        bool canSelectAll,
        bool canUndo,
        bool canRedo)
    {
        _editAvailability = new EditAvailability(
            canCopy,
            canCut,
            canPaste,
            canSelectAll,
            canUndo,
            canRedo);
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Coalesce concurrent reload requests. FileSystemWatcher commonly emits
        // duplicate Changed events for one logical write; a second reload arriving
        // mid-flight folds into one follow-up pass instead of racing the first.
        lock (_reloadLock)
        {
            if (_isReloadInProgress)
            {
                _hasPendingReload = true;
                return;
            }
            _isReloadInProgress = true;
        }

        while (true)
        {
            // async void: catch everything so a faulty editor cannot crash the process.
            try
            {
                await ReloadWithStatePreservationAsync();
                // Sync the ViewModel's external-change tracking with the disk
                // content we just loaded so duplicate watcher events for this
                // write hash-match the cache on the next iteration.
                await _viewModel.UpdateFileTrackingInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "External reload failed for contribution document");
            }

            // Drain any pending request. Skip the follow-up reload when the disk
            // content has not actually changed since the reload we just ran (the
            // duplicate-watcher-event case).
            bool runFollowUp = false;
            while (!runFollowUp)
            {
                bool wasPending;
                lock (_reloadLock)
                {
                    wasPending = _hasPendingReload;
                    _hasPendingReload = false;
                    if (!wasPending)
                    {
                        _isReloadInProgress = false;
                        return;
                    }
                }

                try
                {
                    runFollowUp = await _viewModel.IsFileChangedExternallyAsync();
                }
                catch (Exception ex)
                {
                    // Treat a failed disk probe as "assume changed" so we don't
                    // silently drop a legitimately-queued reload.
                    _logger.LogDebug(ex, "External change probe failed; running follow-up reload defensively");
                    runFollowUp = true;
                }
            }
        }
    }

    private void WarnOnEmptyPackageSecrets()
    {
        Guard.IsNotNull(Contribution);

        // An empty secret value almost certainly indicates a missing private license file or a module that
        // failed to populate its BundledPackageDescriptor. The editor at the other end will typically fail
        // to activate, so surface it loudly here.
        foreach (var pair in Contribution.Package.Secrets)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                _logger.LogWarning(
                    "Secret '{SecretName}' for package '{PackageName}' is empty; the editor will likely fail to activate.",
                    pair.Key, Contribution.Package.Name);
            }
        }
    }

    // Builds the capability context (permitted tools, secrets, options) that the JS client fetches over the
    // bridge via host/getContext on every head.
    public CelbridgeContext GetContext()
    {
        return BuildCelbridgeContext();
    }

    private CelbridgeContext BuildCelbridgeContext()
    {
        Guard.IsNotNull(Contribution);

        return new CelbridgeContext(
            Contribution.Package.PermittedTools,
            Contribution.Package.Secrets,
            Contribution.Options);
    }
}
