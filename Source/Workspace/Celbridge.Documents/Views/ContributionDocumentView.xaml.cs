using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
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

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for contribution-based editors, hosted via a WebView2.
/// </summary>
public sealed partial class ContributionDocumentView : DocumentView, IHostInput, IHostContext
{
    private const int SaveRequestTimeoutSeconds = 30;
    private const int ReloadStateWaitSeconds = 5;

    // Editor-state capture/restore is best-effort (a user convenience, not data). Bound the wait so an
    // editor that never responds to the host->editor RPC cannot stall document close, which would jam
    // the serial command queue. Kept under the command watchdog's 5s threshold.
    private const int EditorStateRequestTimeoutSeconds = 3;

    private readonly ILogger<ContributionDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;

    private readonly ContributionDocumentViewModel _viewModel;

    private ContributionDocumentHandler? _documentHandler;
    private ContributionToolsHandler? _toolsHandler;

    // JSON-RPC infrastructure
    private WebViewHostChannel? _hostChannel;

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

        _viewModel = serviceProvider.GetRequiredService<ContributionDocumentViewModel>();

        this.InitializeComponent();

        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChangedMessage);

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

        try
        {
            // Acquire a WebView from the factory and add it to the container.
            WebView = await _webViewFactory.AcquireAsync();
            ContributionWebViewContainer.Children.Add(WebView);

            // DevTools is off when the hosting package blocks it (sensitive material)
            // or when the user has not enabled the WebViewDevTools feature flag.
            var devToolsBlocked = Contribution.Package.DevToolsBlocked;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled =
                !devToolsBlocked && _webViewService.IsDevToolsFeatureEnabled();

            WebView.GotFocus -= WebView_GotFocus;
            WebView.GotFocus += WebView_GotFocus;

            // Loopback-served packages are addressed over the file server (the /package/, /project/,
            // and /assets/ routes) and run on every head; the rest still use the in-process virtual
            // host, which only works on the Windows heads. See PackageInfo.ServedViaLoopback.
            var servedViaLoopback = Contribution.Package.ServedViaLoopback;
            var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
            var packageUrlName = Contribution.Package.Name.Replace('.', '-');

            if (servedViaLoopback)
            {
                // The project and shared assets are registered globally by the server; only the
                // package's own folder needs registering here. The document loads from the loopback
                // origin, so the page resolves all root-relative references against the file server.
                fileServer.RegisterPackageFolder(packageUrlName, Contribution.Package.PackageFolder);
            }
            else
            {
                // Map the package's asset folder to a virtual host
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    Contribution.Package.HostName,
                    Contribution.Package.PackageFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                // Map the project folder for resource key path resolution
                var projectFolder = ResourceRegistry.ProjectFolderPath;
                if (!string.IsNullOrEmpty(projectFolder))
                {
                    WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "project.celbridge",
                        projectFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                }
            }

            ApplyThemeToWebView();

            await InjectCelbridgeContextAsync();

            // Inject the in-page tool bridge shim for the webview_* MCP tool namespace.
            // Skipped when the package opts out via DevToolsBlocked (sensitive material)
            // so that no tool surface is exposed for those packages.
            if (!devToolsBlocked)
            {
                await TryInjectToolBridgeShimAsync();
            }

            // Block all navigations except the package's own origin. Each allowed
            // navigation also resets the tool bridge's content-ready gate so webview_*
            // tool calls block until the editor signals readiness post-navigation.
            var allowedNavigationPrefix = servedViaLoopback
                ? fileServer.GetPackageUrl(packageUrlName, string.Empty)
                : $"https://{Contribution.Package.HostName}/";
            WebView.NavigationStarting += (s, args) =>
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

            // Wire up the JSON-RPC host channel for WebView communication.
            _hostChannel = new WebViewHostChannel(WebView.CoreWebView2);
            Host = new CelbridgeHost(_hostChannel);

            Host.AddLocalRpcTarget<IHostInput>(this);
            Host.AddLocalRpcTarget<IHostContext>(this);

            _documentHandler = new ContributionDocumentHandler(
                _viewModel,
                _logger,
                CreateDocumentMetadata,    // Callback to construct document metadata on demand
                () => WritableState,       // Callback to read the current writable state at initialize time
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

            // Register with the WebView tool bridge so the webview_* MCP tools can
            // target this WebView by resource key. Mirrors the shim injection guard.
            if (!devToolsBlocked)
            {
                TryRegisterWithToolBridge();
            }

            var entryPoint = Contribution.EntryPoint;
            var entryUrl = servedViaLoopback
                ? fileServer.GetPackageUrl(packageUrlName, entryPoint)
                : $"https://{Contribution.Package.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);

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

        Host?.Dispose();
        _hostChannel?.Detach();

        Host = null;
        _hostChannel = null;
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

#if WINDOWS
        // Packaged WinUI: install the shim as a document-start script so it runs before page scripts
        // on every navigation.
        try
        {
            var script = toolBridge.GetShimScript();
            await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject WebView tool bridge shim into contribution WebView");
        }
#else
        // Skia: AddScriptToExecuteOnDocumentCreatedAsync is not implemented (and awaiting the faulted
        // operation can stall the WebView init), so the shim is re-delivered per navigation via
        // ExecuteScriptAsync. NavigationCompleted fires before the editor's notifyContentLoaded opens
        // the content-ready gate, so webview_* tool calls find the shim present.
        coreWebView2.NavigationCompleted += OnSkiaNavigationCompleted_ReinjectShim;
        await Task.CompletedTask;
#endif
    }

#if !WINDOWS
    private async void OnSkiaNavigationCompleted_ReinjectShim(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        // async void event handler: swallow exceptions to protect the process. The bridge caches the
        // shim after the first read, so re-reading per navigation is cheap, and the shim is idempotent.
        if (_toolBridge is null)
        {
            return;
        }

        try
        {
            var script = _toolBridge.GetShimScript();
            await sender.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-inject the WebView tool bridge shim on the Skia head");
        }
    }
#endif

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

    private void OnThemeChangedMessage(object recipient, ThemeChangedMessage message)
    {
        if (WebView?.CoreWebView2 is not null)
        {
            ApplyThemeToWebView();
        }
    }

    protected override void OnWritableStateChanged()
    {
        // Skip when the host isn't up yet; the initial state ships through the
        // document/initialize handshake (InitializeResult.WritableState) so the
        // JS client sees it on first load.
        if (Host is null)
        {
            return;
        }

        _ = NotifyWritableStateAsync();
    }

    private async Task NotifyWritableStateAsync()
    {
        try
        {
            await Host!.NotifyWritableStateChangedAsync(WritableState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify contribution of writable-state change");
        }
    }

    private void ApplyThemeToWebView()
    {
        if (WebView?.CoreWebView2 is null)
        {
            return;
        }

        var theme = _userInterfaceService.UserInterfaceTheme;
        try
        {
            // CoreWebView2.Profile is not implemented on the Uno Skia CoreWebView2, so the WebView
            // preferred-color-scheme cannot be set on that head. Editors theme themselves off the
            // prefers-color-scheme media query, which the Skia WebView resolves to the OS theme
            // (its default is Auto). So on Skia the editor follows the system theme, matching the
            // app only when the app theme is set to System; an explicit Dark/Light app override is
            // not reflected in the editor. Driving the editor theme over the bridge to fix that is
            // a deferred enhancement; the mismatch is cosmetic.
            WebView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
        catch (NotImplementedException)
        {
            _logger.LogDebug("CoreWebView2.Profile not available on this head; skipping WebView color scheme");
        }
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

        TeardownWebViewState();

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        // Set this document as the active document when the WebView2 receives focus
        var message = new DocumentViewFocusedMessage(FileResource);
        _messengerService.Send(message);
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

    private async Task InjectCelbridgeContextAsync()
    {
        Guard.IsNotNull(Contribution);

        // An empty secret value almost certainly indicates a missing private license
        // file or a module that failed to populate its BundledPackageDescriptor. The
        // editor at the other end will typically fail to activate so surface it loudly here.
        foreach (var pair in Contribution.Package.Secrets)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                _logger.LogWarning(
                    "Secret '{SecretName}' for package '{PackageName}' is empty; the editor will likely fail to activate.",
                    pair.Key, Contribution.Package.Name);
            }
        }

        var coreWebView2 = WebView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            _logger.LogWarning("Cannot inject celbridge context: CoreWebView2 is not available");
            return;
        }

#if WINDOWS
        // Document-start global injection is the packaged WinUI fast path. The Uno Skia
        // CoreWebView2 does not implement AddScriptToExecuteOnDocumentCreatedAsync, and awaiting
        // the faulted operation can stall the WebView init, so the Skia head omits it: the JS
        // client fetches the context over the bridge via host/getContext (see GetContext).
        var contextJson = JsonSerializer.Serialize(BuildCelbridgeContext(), ContextSerializerOptions);
        var script = $"window.__celbridgeContext = {contextJson};";
        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
#else
        await Task.CompletedTask;
#endif
    }

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

#if WINDOWS
    // Used only for the packaged WinUI document-start context injection (see InjectCelbridgeContextAsync).
    private static readonly JsonSerializerOptions ContextSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
#endif
}
