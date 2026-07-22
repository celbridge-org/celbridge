using System.Text.Json;
using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Host;
using Celbridge.Logging;
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
/// The hosting surface a consumer supplies to a CustomEditorController: the workspace panel the
/// editor's web surface reports focus through, and the side effect to run when it gains focus.
/// </summary>
public sealed record CustomEditorFocusContext(WorkspacePanel Panel, Action OnFocusGained);

/// <summary>
/// Drives a custom (WebView-based) editor: it acquires and tears down the WebView, owns the JSON-RPC
/// host channel and its RPC targets, mirrors app/view state, bridges the webview_* tools, coordinates saves
/// and external reloads, and implements the edit-target and link-routing behaviour.
/// </summary>
public sealed class CustomEditorController : IHostInput, IHostContext, IEditTarget
{
    private const int SaveRequestTimeoutSeconds = 30;
    private const int ReloadStateWaitSeconds = 5;

    // Editor-state capture/restore is best-effort (a user convenience, not data). Bound the wait so an
    // editor that never responds to the host->editor RPC cannot stall document close, which would jam
    // the serial command queue. Kept under the command watchdog's 5s threshold.
    private const int EditorStateRequestTimeoutSeconds = 3;

    private readonly ILogger<CustomEditorController> _logger;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;

    private readonly CustomDocumentViewModel _viewModel;

    // The container the WebView currently lives in, and the focus identity it reports through. Both are
    // reassigned by Redock when a utility moves between dock locations. The WebView is moved, never rebuilt.
    private Panel _webViewContainer;
    private CustomEditorFocusContext _focusContext;

    // Set by InitializeAsync before the WebView is configured.
    private ResolvedEditor? _resolvedEditor;
    private EditorContribution? _contribution;

    // Writable state mirrored to the WebView over the viewState channel. Seeded at init and updated by the
    // consumer via SetWritableState.
    private WritableState _writableState = WritableState.Writable;

    // Latest edit availability reported by the editor over the bridge. Drives CanPerformEdit.
    private EditAvailability _editAvailability = EditAvailability.None;

    private CustomDocumentHandler? _documentHandler;
    private PackageToolsHandler? _toolsHandler;
    private IDisposable? _appStateConnection;

    // This editor's own state store (writability), mirrored to its WebView over the viewState channel.
    private IStateStore? _viewState;
    private IDisposable? _viewStateConnection;

    // JSON-RPC infrastructure. Teardown detaches the WebView2 channel or disposes the deferred
    // WebSocket channel, depending on the transport the host channel factory selected.
    private Action? _hostChannelTeardown;

    // Set when the WebSocket transport is in use, to resync the editor after a reconnect.
    private ProxyHostChannel? _proxyChannel;

    // WebView tool bridge registration tracking. Only set when the package allows the
    // webview_* tools and the registration has succeeded. The field doubles as a guard
    // for unregistration.
    private IDocumentWebViewToolBridge? _toolBridge;
    private ResourceKey _toolBridgeRegisteredResource;

    // Deferred editor state for views where the WebView initializes asynchronously.
    // RestoreEditorStateAsync stores state here when the editor isn't ready yet,
    // and SetContentLoaded applies it once the JS client signals readiness.
    private string? _pendingEditorStateJson;
    private bool _isContentLoaded;

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

    // Set when the host channel re-binds a reconnected transport. Forces the next reload to run even
    // when the disk content matches the file tracking info, because the editor may have missed a reload
    // notification that was in transit when the previous transport died.
    private bool _forceReload;

    // Completed by InitCustomViewAsync with the init outcome. InitializeAsync
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

    public CustomEditorController(
        IServiceProvider serviceProvider,
        CustomDocumentViewModel viewModel,
        Panel webViewContainer,
        CustomEditorFocusContext focusContext)
    {
        _serviceProvider = serviceProvider;
        _viewModel = viewModel;
        _webViewContainer = webViewContainer;
        _focusContext = focusContext;

        _logger = serviceProvider.GetRequiredService<ILogger<CustomEditorController>>();
        _commandService = serviceProvider.GetRequiredService<ICommandService>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        _dialogService = serviceProvider.GetRequiredService<IDialogService>();
        _webViewFactory = serviceProvider.GetRequiredService<IWebViewFactory>();
        _webViewService = serviceProvider.GetRequiredService<IWebViewService>();
        _webViewAdapter = ServiceLocator.AcquireService<IWebViewAdapter>();
        _webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();

        _viewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    /// <summary>
    /// Initializes the given resolved editor: acquires and configures the WebView and host, then
    /// completes when the WebView and host are ready for RPCs. The init runs once, and later calls await the
    /// same result. The editor's own notifyContentLoaded signal is not awaited here.
    /// </summary>
    public async Task<Result> InitializeAsync(ResolvedEditor resolvedEditor)
    {
        _resolvedEditor = resolvedEditor;
        _contribution = resolvedEditor.Contribution;
        _viewModel.Contribution = resolvedEditor.Contribution;

        if (_initTcs is null)
        {
            _initTcs = new TaskCompletionSource<Result>();
            _ = InitCustomViewAsync();
        }

        var initResult = await _initTcs.Task;
        if (initResult.IsFailure)
        {
            return initResult;
        }

        return Result.Ok();
    }

    public bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    /// <summary>
    /// Saves the editor content by asking the WebView to flush its state, then writing it through the view
    /// model. Coalesces concurrent saves and abandons the request if the editor does not respond in time.
    /// </summary>
    public async Task<Result> SaveContentAsync()
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

            var errorMessage = $"Custom editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
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

    private ICustomEditorLoader ResolveCustomEditorLoader(PackageInfo package)
    {
        // The loopback default is registered first and matches every package, so it is the fallback. A
        // module's custom loader is registered later and wins as the last matching loader.
        ICustomEditorLoader? selected = null;
        foreach (var candidate in _serviceProvider.GetServices<ICustomEditorLoader>())
        {
            if (candidate.CanLoad(package))
            {
                selected = candidate;
            }
        }

        Guard.IsNotNull(selected);

        return selected;
    }

    private async Task InitCustomViewAsync()
    {
        if (_contribution is null)
        {
            var error = "Cannot initialize custom view: Contribution is not set";
            _logger.LogError(error);
            _initTcs!.TrySetResult(Result.Fail(error));
            return;
        }

        var editorLoader = ResolveCustomEditorLoader(_contribution.Package);

        // The factory hands back a control with CoreWebView2 already live (pre-warmed where possible), so
        // the host is configured immediately and init is signalled once the editor's navigation has started.
        try
        {
            WebView = await _webViewFactory.AcquireAsync();
            _webViewContainer.Children.Add(WebView);

            await ConfigureWebViewHostAsync(editorLoader);

            _initTcs!.TrySetResult(Result.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize custom view: {_contribution.Package.Name}");
            TeardownWebViewState();
            var failure = Result.Fail($"Failed to initialize custom view: {_contribution.Package.Name}")
                .WithException(ex);
            _initTcs!.TrySetResult(failure);
        }
    }

    // Configures a live WebView (CoreWebView2 ready): host channel, RPC targets, tool bridge, navigation gate,
    // and the editor load.
    private async Task ConfigureWebViewHostAsync(ICustomEditorLoader editorLoader)
    {
        Guard.IsNotNull(_contribution);
        Guard.IsNotNull(WebView);

        // DevTools is off when the hosting package blocks it (sensitive material)
        // or when the user has not enabled the WebViewDevTools feature flag.
        var devToolsBlocked = _contribution.Package.DevToolsBlocked;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled =
            !devToolsBlocked && _webViewService.IsDevToolsFeatureEnabled();

        // Register this editor's web surface. It hosts an edit target (this) for the Edit commands.
        RegisterWebSurfaceFocus();

        // Every custom editor's assets (its lib, the shared client) are served from the loopback
        // file server, so register this package's folder there. The resolved loader decides where the
        // entry page itself loads from.
        var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
        var packageUrlName = _contribution.Package.Name.Replace('.', '-');

        fileServer.RegisterPackageFolder(packageUrlName, _contribution.Package.PackageFolder);

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
        var useWebSocketChannel = editorLoader.GetTransport(_contribution.Package) == HostChannelTransport.LoopbackWebSocket;
        var hostChannelBroker = _serviceProvider.GetRequiredService<IHostChannelBroker>();
        var hostChannelSetup = HostChannelFactory.Create(WebView.CoreWebView2, useWebSocketChannel, hostChannelBroker);
        _hostChannelTeardown = hostChannelSetup.Teardown;
        var connectionToken = hostChannelSetup.ConnectionToken;
        Host = new CelbridgeHost(hostChannelSetup.Channel);

        // A reconnected transport (e.g. after an OS suspend dropped the socket) may have lost messages
        // that were in transit when the previous socket died, so resync the editor on every rebind.
        if (hostChannelSetup.Channel is ProxyHostChannel proxyChannel)
        {
            _proxyChannel = proxyChannel;
            proxyChannel.Rebound += OnHostChannelRebound;
        }

        Host.AddLocalRpcTarget<IHostInput>(this);
        Host.AddLocalRpcTarget<IHostContext>(this);

        _documentHandler = new CustomDocumentHandler(
            _viewModel,
            _logger,
            CreateDocumentMetadata,    // Callback to construct document metadata on demand
            CompleteSave);             // Callback to update state when saving has completed

        _documentHandler.ContentLoaded += SetContentLoaded;

        var dialogHandler = new CustomDialogHandler(
            _dialogService,
            _stringLocalizer,
            _viewModel);

        Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
        Host.AddLocalRpcTarget<IHostDialog>(dialogHandler);

        var mcpToolBridge = _serviceProvider.GetService<IMcpToolBridge>();
        if (mcpToolBridge is not null)
        {
            _toolsHandler = new PackageToolsHandler(mcpToolBridge, _contribution.Package.PermittedTools);
            Host.AddLocalRpcTarget<PackageToolsHandler>(_toolsHandler);
        }

        Host.StartListening();

        // Registering pushes the current snapshot, so it must run after StartListening. Seed writability
        // before registering so the connect push carries it.
        var stateService = _serviceProvider.GetRequiredService<IWebViewStateService>();
        var capturedHost = Host;
        _appStateConnection = stateService.AppState.RegisterConnection(
            snapshot => capturedHost.Rpc.NotifyWithParameterObjectAsync(StateRpcMethods.AppStateChanged, snapshot));

        _viewState = stateService.CreateViewState();
        _viewState.SetValue("writable", _writableState.ToString());
        // The preview find bar is built only where the WebView backend has no find bar of its own. Where it
        // does (Chromium's WebView2), the package stays hands-off and Ctrl+F reaches the built-in bar.
        _viewState.SetValue("providesBuiltInFind", _webViewAdapter.ProvidesBuiltInFind ? "true" : "false");
        _viewStateConnection = _viewState.RegisterConnection(
            snapshot => capturedHost.Rpc.NotifyWithParameterObjectAsync(StateRpcMethods.ViewStateChanged, snapshot));

        // Register with the WebView tool bridge so the webview_* MCP tools can
        // target this WebView by resource key. Mirrors the shim injection guard.
        if (!devToolsBlocked)
        {
            TryRegisterWithToolBridge();
        }

        var entryPoint = _contribution.EntryPoint;
        var serverPort = _serviceProvider.GetRequiredService<IServerService>().Port;
        var loadRequest = new CustomEditorLoadRequest(
            WebView!,
            _contribution.Package,
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

            if (uri.StartsWith(allowedNavigationPrefix))
            {
                _toolBridge?.NotifyContentLoading(_toolBridgeRegisteredResource);
                return;
            }

            args.Cancel = true;
        };

        await editorLoader.LoadAsync(loadRequest);
    }

    // Registers this editor's web surface with the focus registry using the consumer-supplied panel identity
    // and focus-gained side effect. The controller is the surface's edit target and owns the DOM focus release.
    private void RegisterWebSurfaceFocus()
    {
        Guard.IsNotNull(WebView);

        var registration = new WebViewFocusRegistration(
            WebView,
            _focusContext.Panel,
            EditTarget: this,
            ReleaseFocus: ReleaseFocus,
            OnFocusGained: _focusContext.OnFocusGained);

        _webViewFocusRegistry.Register(registration);
    }

    /// <summary>
    /// Moves the live WebView into a new container and re-points its focus registration at it, without tearing
    /// it down or reloading it. This is the dock primitive: a utility keeps one WebView (and all its live state)
    /// while it moves between dock locations (the Utility Panel and a document tab). Called before the WebView
    /// is acquired, it just records the target container so the pending init lands there.
    /// </summary>
    public void Redock(Panel newContainer, CustomEditorFocusContext focusContext)
    {
        _focusContext = focusContext;

        if (WebView is null)
        {
            // Not yet initialized. The pending init adds the WebView to this container and registers focus.
            _webViewContainer = newContainer;
            return;
        }

        // Drop the old registration first so the registry never holds a stale host identity.
        if (WebView.CoreWebView2 is not null)
        {
            _webViewFocusRegistry.Unregister(WebView.CoreWebView2);
        }

        if (!ReferenceEquals(newContainer, _webViewContainer))
        {
            _webViewContainer.Children.Remove(WebView);
            newContainer.Children.Add(WebView);
            _webViewContainer = newContainer;
        }

        RegisterWebSurfaceFocus();
    }

    /// <summary>
    /// Tears down the editor: unsubscribes from the view model, resets content-loaded state, and disposes the
    /// WebView, host channel, and associated handlers. Safe to call multiple times.
    /// </summary>
    public void Teardown()
    {
        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;

        _isContentLoaded = false;
        _pendingEditorStateJson = null;

        TeardownWebViewState();
    }

    // Tears down the WebView, host channel, and associated handlers. Safe to call multiple times and from
    // partially initialized states.
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
            if (WebView.CoreWebView2 is not null)
            {
                _webViewFocusRegistry.Unregister(WebView.CoreWebView2);
            }

            _webViewAdapter.CloseWebView(WebView, _webViewContainer);

            WebView = null;
        }

        if (_proxyChannel is not null)
        {
            _proxyChannel.Rebound -= OnHostChannelRebound;
            _proxyChannel = null;
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
            await _webViewAdapter.InstallDocumentStartScriptAsync(coreWebView2, script);
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
            await _webViewAdapter.ReinjectDocumentStartScriptAsync(sender, script);
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

        var resource = _viewModel.FileResource;
        if (resource.IsEmpty)
        {
            return;
        }

        toolBridge.RegisterWebView2(resource, webView, _webViewAdapter);

        _toolBridge = toolBridge;
        _toolBridgeRegisteredResource = resource;
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    /// <summary>
    /// The writable state last applied to the editor. Used by the save tick to suppress expected read-only
    /// save failures.
    /// </summary>
    public WritableState WritableState => _writableState;

    /// <summary>
    /// Applies a writable state: stores it and mirrors it to the WebView over the viewState channel. The store
    /// may not exist yet (set before init), in which case the seed at registration captures the current value.
    /// </summary>
    public void SetWritableState(WritableState state)
    {
        _writableState = state;
        _viewState?.SetValue("writable", state.ToString());
    }

    private DocumentMetadata CreateDocumentMetadata()
    {
        var locale = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var metaData = new DocumentMetadata(
            _viewModel.FilePath,
            _viewModel.FileResource.ToString(),
            Path.GetFileName(_viewModel.FilePath),
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
                // Expected while the editor transport is mid-reconnect. The rebind resync re-runs the reload.
                _logger.LogWarning(
                    "Editor did not confirm external reload within {Seconds}s. File: {File}",
                    ReloadStateWaitSeconds, _viewModel.FilePath);
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

    public async Task<string?> TrySaveEditorStateAsync()
    {
        if (Host is null || !_isContentLoaded)
        {
            return null;
        }

        // Race the request against a hard timeout and abandon it on timeout. A CancellationToken does
        // not work here: StreamJsonRpc cancellation waits for the editor to acknowledge the cancel, and
        // the unresponsive editor is exactly the failure being guarded against.
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

    public async Task RestoreEditorStateAsync(string state)
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
            // Best-effort restore. An unresponsive editor should not stall the caller. Abandon and move on.
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

        if (_contribution is null)
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

    /// <summary>
    /// Sends a navigate-to-location request to the editor. The location is a JSON object describing the target
    /// line and column range. A null host or empty location is a no-op.
    /// </summary>
    public async Task<Result> NavigateToLocationAsync(string location)
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

    /// <summary>
    /// Gives the editor's web content keyboard focus and reports it to the focus service.
    /// </summary>
    public void FocusWebView()
    {
        // A tab click focuses the web content (native first responder on macOS, where no managed GotFocus
        // follows). The registry gives it focus and reports it, releasing the previously focused surface.
        if (WebView is not null)
        {
            _webViewFocusRegistry.GrantFocus(WebView);
        }
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

    public bool TryHandleTabKey(bool shift)
    {
        // A code editor with text focus indents or outdents. It reports that over the bridge, so a read-only
        // or unfocused editor falls through to the generic Tab notification below.
        if (_editAvailability.CanIndent)
        {
            var command = shift ? "outdent" : "indent";
            _ = Host?.NotifyPerformEditAsync(command);

            return true;
        }

        // Other editors handle Tab their own way (the spreadsheet moves the active cell). Editors that do
        // not act on it ignore the notification, and the key is still swallowed so focus stays in the document.
        _ = Host?.NotifyTabKeyAsync(shift);

        return true;
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
        bool canRedo,
        bool canIndent = false)
    {
        _editAvailability = new EditAvailability(
            canCopy,
            canCut,
            canPaste,
            canSelectAll,
            canUndo,
            canRedo,
            canIndent);
    }

    private void OnHostChannelRebound(object? sender, EventArgs e)
    {
        lock (_reloadLock)
        {
            _forceReload = true;
        }

        // Raised on the WebSocket endpoint's request thread, so marshal to the UI thread where the
        // reload pipeline runs.
        _webViewContainer.DispatcherQueue.TryEnqueue(() => ViewModel_ReloadRequested(this, EventArgs.Empty));
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Coalesce concurrent reload requests. FileSystemWatcher commonly emits
        // duplicate Changed events for one logical write. A second reload arriving
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
            lock (_reloadLock)
            {
                _forceReload = false;
            }

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
                _logger.LogError(ex, "External reload failed for custom editor");
            }

            // Drain any pending request. Skip the follow-up reload when the disk
            // content has not actually changed since the reload we just ran (the
            // duplicate-watcher-event case) -- unless a transport rebind forced it,
            // in which case the editor may be stale even though the tracking info
            // matches the disk.
            bool runFollowUp = false;
            while (!runFollowUp)
            {
                bool wasPending;
                bool forceReload;
                lock (_reloadLock)
                {
                    wasPending = _hasPendingReload;
                    _hasPendingReload = false;
                    forceReload = _forceReload;
                    if (!wasPending)
                    {
                        _isReloadInProgress = false;
                        return;
                    }
                }

                if (forceReload)
                {
                    runFollowUp = true;
                    continue;
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
        Guard.IsNotNull(_contribution);

        // An empty secret value almost certainly indicates a missing private license file or a module that
        // failed to populate its BundledPackageDescriptor. The editor at the other end will typically fail
        // to activate, so surface it loudly here.
        foreach (var pair in _contribution.Package.Secrets)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                _logger.LogWarning(
                    "Secret '{SecretName}' for package '{PackageName}' is empty; the editor will likely fail to activate.",
                    pair.Key, _contribution.Package.Name);
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
        Guard.IsNotNull(_resolvedEditor);
        Guard.IsNotNull(_contribution);

        // The editor's effective config (manifest options overlaid with descriptor defaults
        // and its project-config keys) rides the Options channel.
        return new CelbridgeContext(
            _contribution.Package.PermittedTools,
            _contribution.Package.Secrets,
            _resolvedEditor.Config);
    }
}
