using System.Text.Json;
using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Documents.Helpers;
using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Server;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Documents.Views;

/// <summary>
/// The hosting surface a consumer supplies to a CustomEditorController: the workspace panel the
/// editor's web surface reports focus through, and the side effect to run when it gains focus.
/// </summary>
public sealed record CustomEditorFocusContext(FocusPanelId Panel, Action OnFocusGained);

/// <summary>
/// Drives a custom (WebView-based) editor: it acquires and disposes its web view, owns the JSON-RPC
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
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IWebSurfaceLog _webSurfaceLog;
    private readonly ILanguageService _languageService;

    private readonly CustomDocumentViewModel _viewModel;

    // The container the web view currently lives in, and the focus identity it reports through. Both are
    // reassigned by Redock when a utility moves between areas. The web view is moved, never rebuilt.
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

    // A package-declared channel (e.g. the console terminal I/O bridge) registered as an additional RPC
    // target set, plus the host adapter it talks through. Null for editors whose package declares no
    // channel.
    private ICustomEditorChannel? _channel;
    private CustomEditorChannelHost? _channelHost;

    private IDisposable? _appStateConnection;

    // This editor's own state store, mirrored to its WebView over the viewState channel.
    private IStateStore? _viewState;
    private IDisposable? _viewStateConnection;

    // The presented size, kept until the web view exists.
    private double _presentedWidth;
    private double _presentedHeight;

    // The channel the JSON-RPC host runs on. The page's WebSocket binds to it when the page connects, and
    // again after a reconnect.
    private ProxyHostChannel? _proxyChannel;

    // The tool bridge the web view is registered with, or null when the package blocks the webview_* tools.
    private IWebViewToolBridge? _toolBridge;

    // The web page the editor runs in.
    private readonly EditorPage _page = new();

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

    // Set by Teardown. A web view that arrives after that is disposed straight away.
    private bool _isTornDown;

    // The web view the editor runs in, acquired from the factory.
    private IEditorWebView? _webView;

    // The web view's accessible name, stored until the web view is acquired.
    private string _accessibleName = string.Empty;

    // The editor's own origin. Every other navigation is cancelled.
    private string _allowedNavigationPrefix = string.Empty;

    // The Celbridge host for JSON-RPC communication with the WebView.
    private CelbridgeHost? Host { get; set; }

    public WebViewHealth GetHealth()
    {
        return _webView?.GetHealth() ?? WebViewHealth.Healthy;
    }

    /// <summary>
    /// The view model the controller reports content changes to.
    /// </summary>
    public CustomDocumentViewModel ViewModel => _viewModel;

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
        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _projectService = serviceProvider.GetRequiredService<IProjectService>();
        _reportWriter = serviceProvider.GetRequiredService<IReportWriter>();
        _webViewFactory = serviceProvider.GetRequiredService<IWebViewFactory>();
        _webViewService = serviceProvider.GetRequiredService<IWebViewService>();
        _webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();
        _webSurfaceLog = ServiceLocator.AcquireService<IWebSurfaceLog>();
        _languageService = ServiceLocator.AcquireService<ILanguageService>();

        _viewModel.ReloadRequested += ViewModel_ReloadRequested;

        _messengerService.Register<LanguageChangedMessage>(this, OnLanguageChanged);
    }

    private void OnLanguageChanged(object recipient, LanguageChangedMessage message)
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        // Fire and forget: an editor that fails to re-localize keeps working in the previous language, which
        // is not worth failing a language change over.
        _ = host.NotifyLanguageChangedAsync(message.Language);
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

    /// <summary>
    /// Sets the web view's accessible name, now or once the web view is acquired.
    /// </summary>
    public void SetAccessibleName(string name)
    {
        _accessibleName = name;
        _webView?.SetAccessibleName(name);
    }

    /// <summary>
    /// Sets the resource the web view shows. A web view acquired later takes the view model's resource.
    /// </summary>
    public void SetResource(ResourceKey resource)
    {
        _webView?.SetResource(resource);
    }

    /// <summary>
    /// Tells the editor page the document's new name and path. Called after a rename, which reuses this
    /// controller and its page, so the page would otherwise keep the name it was given when it opened.
    /// </summary>
    public void NotifyRenamed()
    {
        if (Host is null)
        {
            return;
        }

        // A page still loading is not listening for the rename yet, so it waits for the page's load.
        if (_page.TryDeferRename())
        {
            _logger.LogDebug("Deferred a rename until the editor page loads: {Resource}", _viewModel.FileResource);
            return;
        }

        _ = SendRenamedAsync(Host);
    }

    private async Task SendRenamedAsync(CelbridgeHost host)
    {
        try
        {
            _logger.LogDebug("Telling the editor page about a rename: {Resource}", _viewModel.FileResource);
            await host.NotifyRenamedAsync(CreateDocumentMetadata());
        }
        catch (Exception ex)
        {
            // The rename has already happened, so a page that cannot be reached is logged rather than failing it.
            _logger.LogWarning(ex, "Failed to tell the editor page about a rename: {File}", _viewModel.FilePath);
        }
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

        // The factory returns a view whose page is ready, often prewarmed. The host is configured at once, and
        // init completes when the editor's navigation starts.
        try
        {
            var webView = await _webViewFactory.AcquireAsync(CreateWebViewOptions());
            if (_isTornDown)
            {
                webView.Dispose();
                _initTcs!.TrySetResult(Result.Fail($"The editor closed before its web view was ready: {_contribution.Package.Name}"));
                return;
            }

            _webView = webView;
            webView.SetResource(_viewModel.FileResource);
            webView.SetAccessibleName(_accessibleName);

            webView.IsSizedChanged += WebView_IsSizedChanged;

            webView.AttachTo(_webViewContainer);

            // The section may have reported its size before the view existed.
            webView.SetPresentedSize(_presentedWidth, _presentedHeight);

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

    private WebViewOptions CreateWebViewOptions()
    {
        Guard.IsNotNull(_contribution);

        // DevTools is off when the hosting package blocks it (sensitive material)
        // or when the user has not enabled the WebViewDevTools feature flag.
        var devToolsEnabled = !_contribution.Package.DevToolsBlocked && _webViewService.IsDevToolsFeatureEnabled();

        // A custom editor is application chrome, not a browsable page, so zoom is off. It follows OS display
        // scaling like the native panels.
        return new WebViewOptions
        {
            IsDevToolsEnabled = devToolsEnabled,
            IsZoomEnabled = false,
        };
    }

    // Configures a live web view: host channel, RPC targets, tool bridge, navigation gate, and the editor load.
    private async Task ConfigureWebViewHostAsync(ICustomEditorLoader editorLoader)
    {
        Guard.IsNotNull(_contribution);
        Guard.IsNotNull(_webView);

        var webView = _webView;
        var devToolsBlocked = _contribution.Package.DevToolsBlocked;

        // Register this editor's web surface. It hosts an edit target (this) for the Edit commands.
        RegisterWebSurfaceFocus();

        // Every custom editor's assets (its lib, the shared client) are served from the loopback
        // file server, so register this package's folder there. The resolved loader decides where the
        // entry page itself loads from.
        var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
        var packageUrlName = _contribution.Package.Name;

        fileServer.RegisterPackageFolder(packageUrlName, _contribution.Package.PackageFolder);

        WarnOnEmptyPackageSecrets();

        // Inject the in-page tool bridge shim for the webview_* MCP tool namespace.
        // Skipped when the package opts out via DevToolsBlocked (sensitive material)
        // so that no tool surface is exposed for those packages.
        if (!devToolsBlocked)
        {
            await TryInjectToolBridgeShimAsync(webView);
        }

        // Wire up the JSON-RPC host channel. The page opens a WebSocket back to the loopback server, and the
        // connection token in its address binds that socket to this channel.
        var hostChannelBroker = _serviceProvider.GetRequiredService<IHostChannelBroker>();
        var pendingConnection = hostChannelBroker.CreatePendingConnection();
        var connectionToken = pendingConnection.Token;
        _proxyChannel = pendingConnection.Channel;
        var logTarget = new WebSurfaceLogTarget(webView, _webSurfaceLog);
        Host = new CelbridgeHost(_proxyChannel, logTarget);

        // A reconnected transport (e.g. after an OS suspend dropped the socket) may have lost messages
        // that were in transit when the previous socket died, so resync the editor on every rebind.
        _proxyChannel.Rebound += OnHostChannelRebound;

        Host.AddLocalRpcTarget<IHostInput>(this);
        Host.AddLocalRpcTarget<IHostContext>(this);

        _documentHandler = new CustomDocumentHandler(
            _viewModel,
            _logger,
            _projectService,
            _reportWriter,
            CreateDocumentMetadata,    // Callback to construct document metadata on demand
            CompleteSave);             // Callback to update state when saving has completed

        _documentHandler.ContentLoaded += SetContentLoaded;

        var dialogHandler = new CustomDialogHandler(
            _dialogService,
            _stringLocalizer,
            _messengerService,
            _viewModel);

        Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
        Host.AddLocalRpcTarget<IHostDialog>(dialogHandler);

        var mcpToolBridge = _serviceProvider.GetService<IMcpToolBridge>();
        if (mcpToolBridge is not null)
        {
            _toolsHandler = new PackageToolsHandler(mcpToolBridge);
            Host.AddLocalRpcTarget<PackageToolsHandler>(_toolsHandler);
        }

        // Attach a package-declared channel as an additional RPC target set. Must run before StartListening
        // because AddLocalRpcTarget throws once the host is listening. No-op for editors whose package
        // declares no channel provider.
        AttachChannel();

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
        _viewState.SetValue("providesBuiltInFind", webView.ProvidesBuiltInFind ? "true" : "false");
        // A page that measures its viewport before this surface is arranged is reading a placeholder.
        _viewState.SetValue("isSized", webView.IsSized ? "true" : "false");
        // Whether a size can reach a page the platform is not displaying.
        _viewState.SetValue(
            "canSizeUnarranged",
            webView.CanSizeUnarrangedViewport ? "true" : "false");
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
            webView,
            _contribution.Package,
            packageUrlName,
            entryPoint,
            connectionToken,
            serverPort);

        _allowedNavigationPrefix = editorLoader.GetAllowedNavigationOrigin(loadRequest);
        webView.NavigationStarting += WebView_NavigationStarting;

        await editorLoader.LoadAsync(loadRequest);
    }

    // Blocks all navigations except the editor's own origin. A navigation of the editor page itself also
    // resets the tool bridge's content-ready gate so webview_* tool calls block until the new page signals
    // readiness. A frame loading inside the page leaves the gate open.
    private void WebView_NavigationStarting(object? sender, WebNavigationStartingEventArgs args)
    {
        var uri = args.Uri;
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        if (uri.StartsWith(_allowedNavigationPrefix))
        {
            if (_page.OnNavigating(uri)
                && _webView is not null)
            {
                _toolBridge?.NotifyContentLoading(_webView);
            }
            return;
        }

        args.Cancel = true;
    }

    // Resolves the first channel provider that handles this editor's contribution and attaches its channel
    // as an additional RPC target set. Providers are resolved from DI like the custom editor loaders.
    private void AttachChannel()
    {
        Guard.IsNotNull(_contribution);
        Guard.IsNotNull(_resolvedEditor);
        Guard.IsNotNull(Host);
        Guard.IsNotNull(_webView);

        ICustomEditorChannelProvider? provider = null;
        foreach (var candidate in _serviceProvider.GetServices<ICustomEditorChannelProvider>())
        {
            if (candidate.CanCreate(_contribution))
            {
                provider = candidate;
                break;
            }
        }

        if (provider is null)
        {
            return;
        }

        var context = new CustomEditorChannelContext(_resolvedEditor);
        var channel = provider.Create(context);

        _channelHost = new CustomEditorChannelHost(Host, _webView);
        channel.RegisterTargets(_channelHost);
        _channel = channel;
    }

    // Registers this editor's web view with the focus registry using the consumer-supplied panel identity
    // and focus-gained side effect. The controller is the surface's edit target and owns the DOM focus
    // release and its counterpart grant.
    private void RegisterWebSurfaceFocus()
    {
        Guard.IsNotNull(_webView);

        var webViewFocusContext = new WebViewFocusContext(
            _focusContext.Panel,
            EditTarget: this,
            ReleaseFocus: ReleaseFocus,
            GrantDomFocus: GrantDomFocusAsync,
            OnFocusGained: _focusContext.OnFocusGained);

        _webViewFocusRegistry.Register(_webView, webViewFocusContext);
    }

    /// <summary>
    /// Moves the live web view into a new container and re-points its focus registration at it, without
    /// disposing or reloading it. This is the dock primitive: a utility keeps one web view (and all its live
    /// state) while it moves between areas (the Utility Panel and a document tab). Called before the web view
    /// is acquired, it just records the target container so the pending init lands there.
    /// </summary>
    public void Redock(Panel newContainer, CustomEditorFocusContext focusContext)
    {
        _focusContext = focusContext;
        _webViewContainer = newContainer;

        if (_webView is null)
        {
            // Not yet initialized. The pending init adds the web view to this container and registers focus.
            return;
        }

        _webView.AttachTo(newContainer);

        // The registration is replaced rather than dropped and remade: dropping it means the surface is going
        // away, which releases the keyboard it is holding, and a redock is the one case where a live surface
        // changes panel without the user moving focus off it.
        RegisterWebSurfaceFocus();
    }

    /// <summary>
    /// Tears down the editor: unsubscribes from the view model, resets content-loaded state, and disposes the
    /// web view, host channel, and associated handlers. Safe to call multiple times.
    /// </summary>
    public void Teardown()
    {
        _isTornDown = true;

        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;
        _messengerService.Unregister<LanguageChangedMessage>(this);

        _page.Reset();

        TeardownWebViewState();
    }

    // Disposes the web view, host channel, and associated handlers. Safe to call multiple times and from
    // partially initialized states.
    private void TeardownWebViewState()
    {
        // Dispose the channel before the web view and the host: its pty raises output and exit on background
        // threads, so unsubscribing and disposing it must complete before the host it notifies through is gone.
        // Marking the adapter disposed first turns any in-flight outbound notification into a no-op.
        if (_channel is not null)
        {
            _channelHost?.MarkDisposed();
            _channel.Dispose();
            _channel = null;
            _channelHost = null;
        }

        // The tool bridge and the focus registry drop the web view as it closes.
        _toolBridge = null;

        if (_documentHandler is not null)
        {
            _documentHandler.ContentLoaded -= SetContentLoaded;
        }

        _webView?.Dispose();
        _webView = null;

        // The JSON-RPC host is disposed after the web view. It talks to the page over the loopback WebSocket,
        // not through the view.

        var proxyChannel = _proxyChannel;
        if (proxyChannel is not null)
        {
            proxyChannel.Rebound -= OnHostChannelRebound;
            _proxyChannel = null;
        }

        _appStateConnection?.Dispose();
        _viewStateConnection?.Dispose();
        Host?.Dispose();
        proxyChannel?.Dispose();

        _appStateConnection = null;
        _viewStateConnection = null;
        _viewState = null;
        Host = null;
    }

    private async Task TryInjectToolBridgeShimAsync(IEditorWebView webView)
    {
        var toolBridge = _serviceProvider.GetService<IWebViewToolBridge>();
        if (toolBridge is null)
        {
            return;
        }

        // A document-start script wraps console and fetch before the page's scripts run. The get_console and
        // get_network tools need that.
        try
        {
            var script = toolBridge.GetShimScript();
            await webView.AddDocumentStartScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install the document-start WebView tool bridge shim");
        }
    }

    // Tells the page whether its viewport is a real size. It runs only when the answer changes, since the state
    // store pushes every set to the page.
    private void WebView_IsSizedChanged(object? sender, EventArgs e)
    {
        if (sender is not IEditorWebView webView)
        {
            return;
        }

        _viewState?.SetValue("isSized", webView.IsSized ? "true" : "false");
    }

    public void SetPresentedSize(double width, double height)
    {
        if (width <= 0 ||
            height <= 0)
        {
            return;
        }

        _presentedWidth = width;
        _presentedHeight = height;

        _webView?.SetPresentedSize(width, height);
    }

    private void TryRegisterWithToolBridge()
    {
        var webView = _webView;
        if (webView is null)
        {
            return;
        }

        var toolBridge = _serviceProvider.GetService<IWebViewToolBridge>();
        if (toolBridge is null)
        {
            return;
        }

        toolBridge.Register(webView);
        _toolBridge = toolBridge;
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    /// <summary>
    /// The writable state last applied to the editor.
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
        var locale = _languageService.CurrentLanguage;

        // Metadata is made only to send to the page, so this is the document the page is told it shows.
        _page.SetResource(_viewModel.FileResource);

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
        if (_webView is not null)
        {
            _toolBridge?.NotifyContentReady(_webView);
        }

        if (reason != ContentLoadedReason.Initial)
        {
            return;
        }

        var deferred = _page.OnLoaded(_viewModel.FileResource);

        // Applied in the order the opening caller issued them: navigate, then restore state.
        if (deferred.Location is not null)
        {
            var navigateResult = await NavigateToLocationAsync(deferred.Location);
            if (navigateResult.IsFailure)
            {
                _logger.LogWarning(navigateResult, "Failed to navigate to location after content loaded");
            }
        }

        if (deferred.EditorStateJson is not null)
        {
            try
            {
                await RestoreEditorStateAsync(deferred.EditorStateJson);
            }
            catch (Exception ex)
            {
                // Editor state restoration is best-effort: a corrupt or incompatible state should
                // never tear down the process. Log and swallow to preserve the async void safety contract.
                _logger.LogError(ex, "Failed to restore editor state after content loaded");
            }
        }

        // A grant made before the page loaded could not reach it, so it is sent again now. It comes after
        // the state restore because the restored view mode decides whether the editor takes focus.
        if (_webView is not null
            && _webViewFocusRegistry.IsFocusedSurface(_webView))
        {
            _ = GrantDomFocusAsync();
        }

        // What arrived while the page loaded goes to it now that it is listening. The rename comes first, so
        // a reload that follows reads the file under its new name.
        if (deferred.Rename)
        {
            NotifyRenamed();
        }

        if (deferred.Reload)
        {
            await RunDeferredReloadAsync();
        }
    }

    // The page read the file when it loaded, so a reload deferred until then runs only if the file has changed
    // since.
    private async Task RunDeferredReloadAsync()
    {
        bool isFileChanged;
        try
        {
            isFileChanged = await _viewModel.IsFileChangedExternallyAsync();
        }
        catch (Exception ex)
        {
            // A failed probe reloads anyway, so a change is never dropped.
            _logger.LogDebug(ex, "External change probe failed; running the deferred reload");
            isFileChanged = true;
        }

        if (isFileChanged)
        {
            ViewModel_ReloadRequested(this, EventArgs.Empty);
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
        if (Host is null || !_page.IsLoaded)
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
            // Outbound messages are buffered only while no transport is bound, so a bound channel always
            // reports nothing pending.
            var proxyChannel = _proxyChannel;
            var transportState = proxyChannel?.GetTransportState();

            var health = GetHealth();

            _logger.LogWarning(
                "Editor did not return state within {Seconds}s; closing without preserving editor state. File: {File}, proxy channel {HasProxyChannel}, transport bound {IsBound}, pending outbound {PendingOutbound}, missed wakes {WakeFailures}, process failures {ProcessFailures}",
                EditorStateRequestTimeoutSeconds,
                _viewModel.FilePath,
                proxyChannel is not null,
                transportState?.IsBound,
                transportState?.PendingOutboundCount,
                health.WakeFailures,
                health.ProcessFailures);

            AbandonedTaskObserver.Observe(requestStateTask);
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

    public async Task RestoreEditorStateAsync(string state)
    {
        if (_page.TryDeferEditorState(state))
        {
            return;
        }

        var restoreStateTask = Host!.RestoreStateAsync(state);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(EditorStateRequestTimeoutSeconds));
        var completedTask = await Task.WhenAny(restoreStateTask, timeoutTask);

        if (completedTask != restoreStateTask)
        {
            // Best-effort restore. An unresponsive editor should not stall the caller. Abandon and move on.
            _logger.LogWarning("Editor did not acknowledge restoreState within {Seconds}s; continuing.", EditorStateRequestTimeoutSeconds);
            AbandonedTaskObserver.Observe(restoreStateTask);
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
            var workspaceWrapper = _serviceProvider.GetRequiredService<IWorkspaceWrapper>();

            if (!LinkedResourceOpener.Open(_commandService, workspaceWrapper.WorkspaceService, resourceKey))
            {
                _ = ShowLinkErrorAsync(resourceKey.Path);
            }
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

    private async Task ShowLinkErrorAsync(string linkPath)
    {
        var errorTitle = _stringLocalizer.GetString("Extension_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Extension_LinkError_Message", linkPath);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
    }

    /// <summary>
    /// Sends a navigate-to-location request to the editor. The location is a JSON object describing the target
    /// line and column range. An empty location is a no-op, and a request arriving before the editor has
    /// loaded its content is deferred until it has.
    /// </summary>
    public async Task<Result> NavigateToLocationAsync(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        // Opening a document and navigating within it arrive together, but the WebView connects
        // asynchronously, so on a first open the editor is not there to receive this yet.
        if (_page.TryDeferLocation(location)
            || Host is null)
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
                EditorRpcMethods.NavigateToLocation,
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
        if (_webView is null)
        {
            _logger.LogWarning("Cannot focus the editor's web content before its WebView is created");
            return;
        }

        _webViewFocusRegistry.GrantFocus(_webView);
    }

    private void ReleaseFocus()
    {
        _ = Host?.NotifyReleaseFocusAsync();
    }

    // Native focus gives the page the keyboard but leaves no element inside it focused, so an editor that
    // was released when its tab lost focus needs the DOM focus handed back or typing goes nowhere.
    private async Task GrantDomFocusAsync()
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        await host.NotifyGrantFocusAsync();
    }

    public bool CanPerformEdit(EditIntent intent)
    {
        return _editAvailability.Allows(intent);
    }

    public bool HostMediatedClipboard => _editAvailability.HostMediatedClipboard;

    // The editor is a web page, whose own fields the platform edits.
    public bool HasPlatformEditing => true;

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

    // True when the editor has reported a find of its own. The host does not draw a find bar for a custom
    // editor: the editor runs its own, and this is how the host learns it exists.
    public bool CanFind => _editAvailability.CanFind;

    public bool TryBeginFind()
    {
        if (!CanFind
            || Host is null)
        {
            return false;
        }

        _ = Host.NotifyBeginFindAsync();

        return true;
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

        // Other editors handle Tab their own way (the spreadsheet moves the active cell), but only while
        // the keyboard is on the part of the page that does so. Declining hands the key back to the
        // registry, which delivers it to the page natively, so a form field inside the surface moves focus
        // to the next field instead of the surface acting on it.
        if (!_editAvailability.CanHandleTab)
        {
            return false;
        }

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
            var selectedText = await host.Rpc.InvokeAsync<string?>(EditorRpcMethods.GetSelectedText);
            if (string.IsNullOrEmpty(selectedText))
            {
                return;
            }

            // Run the copy now instead of queuing it, so a cut doesn't wait behind other commands.
            var copyResult = await _commandService.ExecuteImmediate<ICopyTextToClipboardCommand>(command =>
            {
                command.Text = selectedText;
            });
            if (copyResult.IsFailure)
            {
                // Keep the selection, so a failed cut loses nothing.
                _logger.LogError(copyResult, "Failed to copy the editor selection to the clipboard");
                return;
            }

            if (deleteSelection)
            {
                // The copy can wait for a busy clipboard, and the selection can change meanwhile. Delete the
                // selection only if it still holds the copied text.
                var currentText = await host.Rpc.InvokeAsync<string?>(EditorRpcMethods.GetSelectedText);
                if (currentText != selectedText)
                {
                    _logger.LogWarning("The selection changed while the cut waited for the clipboard, so the cut kept it");
                    return;
                }

                await host.Rpc.NotifyWithParameterObjectAsync(EditorRpcMethods.InsertText, new { text = string.Empty });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy the editor selection to the clipboard");
        }
    }

    public Task RequestEditAsync(string command)
    {
        // Acts on this editor, which is the surface whose own menu raised the request.
        var intent = command switch
        {
            "cut" => EditIntent.Cut,
            "copy" => EditIntent.Copy,
            "paste" => EditIntent.Paste,
            "selectAll" => EditIntent.SelectAll,
            "undo" => EditIntent.Undo,
            "redo" => EditIntent.Redo,
            _ => (EditIntent?)null
        };

        if (intent is null)
        {
            _logger.LogWarning("Ignored an edit request naming the unknown command '{Command}'", command);
            return Task.CompletedTask;
        }

        PerformEdit(intent.Value);

        return Task.CompletedTask;
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
            if (string.IsNullOrEmpty(text))
            {
                // Editors read an empty insert as a cut's clear step.
                return;
            }

            await host.Rpc.NotifyWithParameterObjectAsync(EditorRpcMethods.InsertText, new { text });
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
        bool canIndent = false,
        bool hostMediatedClipboard = false,
        bool canHandleTab = false,
        bool canFind = false)
    {
        _editAvailability = new EditAvailability(
            canCopy,
            canCut,
            canPaste,
            canSelectAll,
            canUndo,
            canRedo,
            canIndent,
            hostMediatedClipboard,
            canHandleTab,
            canFind);
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
        // A page that has not loaded is not listening for the reload, and reads the file when it loads, so
        // the request waits for that load and then runs only if the file has changed. A reconnect forces its
        // reload for a page that may have missed messages, and a page still loading has missed none.
        if (_page.TryDeferReload())
        {
            lock (_reloadLock)
            {
                _forceReload = false;
            }
            return;
        }

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
                // Sync the ViewModel's external-change tracking with the disk content the editor is about to load,
                // so duplicate watcher events for this write match the cache on the next iteration. It is recorded
                // before the editor reads the file, so a change that lands during the read still differs from it.
                await _viewModel.UpdateFileTrackingInfoAsync();
                await ReloadWithStatePreservationAsync();
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

    // Builds the capability context (secrets, options) that the JS client fetches over the bridge via
    // host/getContext on every head.
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
            _contribution.Package.Secrets,
            _resolvedEditor.Config);
    }
}
