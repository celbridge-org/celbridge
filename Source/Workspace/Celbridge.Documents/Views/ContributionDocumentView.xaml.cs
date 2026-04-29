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
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for contribution-based editors, hosted via a WebView2.
/// </summary>
public sealed partial class ContributionDocumentView : DocumentView, IHostInput
{
    private const int SaveRequestTimeoutSeconds = 30;
    private const int ReloadStateWaitSeconds = 5;

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

            ApplyThemeToWebView();

            await InjectCelbridgeContextAsync(Contribution.Package, Contribution.Options);

            // Inject the in-page tool bridge shim for the webview_* MCP tool namespace.
            // Skipped when the package opts out via DevToolsBlocked (sensitive material)
            // so that no tool surface is exposed for those packages.
            if (!devToolsBlocked)
            {
                await TryInjectToolBridgeShimAsync();
            }

            // Block all navigations except the package's own host name
            var allowedHostPrefix = $"https://{Contribution.Package.HostName}/";
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                if (uri.StartsWith(allowedHostPrefix))
                {
                    return;
                }

                args.Cancel = true;
            };

            // Block all new window requests
            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            // Wire up the JSON-RPC host channel for WebView communication.
            _hostChannel = new WebViewHostChannel(WebView.CoreWebView2);
            Host = new CelbridgeHost(_hostChannel);
            Host.AddLocalRpcTarget<IHostInput>(this);

            _documentHandler = new ContributionDocumentHandler(
                _viewModel,
                _logger,
                CreateDocumentMetadata, // Callback to construct document metadata on demand
                CompleteSave);          // Callback to update state when saving has completed 

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
                _toolsHandler = new ContributionToolsHandler(mcpToolBridge, Contribution.Package.RequiresTools);
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
            var entryUrl = $"https://{Contribution.Package.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);

            _initTcs!.TrySetResult(Result.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize contribution view: {Contribution.Package.Name}");
            TeardownWebViewState();
            var failure = Result.Fail($"Failed to initialize contribution view: {Contribution.Package.Id}")
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

        try
        {
            var script = toolBridge.GetShimScript();
            await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject WebView tool bridge shim into contribution WebView");
        }
    }

    private void TryRegisterWithToolBridge()
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

        var resource = FileResource;
        if (resource.IsEmpty)
        {
            return;
        }

        toolBridge.RegisterCoreWebView2(resource, coreWebView2);

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

    private void ApplyThemeToWebView()
    {
        if (WebView?.CoreWebView2 is null)
        {
            return;
        }

        var theme = _userInterfaceService.UserInterfaceTheme;
        WebView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
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
            await Host.NotifyExternalChangeAsync();

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

        try
        {
            return await Host.RequestStateAsync();
        }
        catch
        {
            return null;
        }
    }

    public override async Task RestoreEditorStateAsync(string state)
    {
        if (!_isContentLoaded)
        {
            _pendingEditorStateJson = state;
            return;
        }

        try
        {
            await Host!.RestoreStateAsync(state);
        }
        catch
        {
            // Editor doesn't implement state restoration
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
        // This method is async void because it's an event handler. All exceptions must be caught
        // so that a faulty editor cannot crash the process.
        try
        {
            await ReloadWithStatePreservationAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External reload failed for contribution document");
        }
    }

    private async Task InjectCelbridgeContextAsync(
        PackageInfo package,
        IReadOnlyDictionary<string, string> options)
    {
        var allowedTools = package.RequiresTools;
        var secrets = package.Secrets;

        // An empty secret value almost certainly indicates a missing private license
        // file or a module that failed to populate its BundledPackageDescriptor. The
        // editor at the other end will typically fail to activate so surface it loudly here.
        foreach (var pair in secrets)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                _logger.LogWarning(
                    "Secret '{SecretName}' for package '{PackageId}' is empty; the editor will likely fail to activate.",
                    pair.Key, package.Id);
            }
        }

        var contextJson = JsonSerializer.Serialize(
            new CelbridgeContext(allowedTools, secrets, options),
            ContextSerializerOptions);

        var coreWebView2 = WebView?.CoreWebView2;
        if (coreWebView2 is null)
        {
            _logger.LogWarning("Cannot inject celbridge context: CoreWebView2 is not available");
            return;
        }

        var script = $"window.__celbridgeContext = {contextJson};";
        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private static readonly JsonSerializerOptions ContextSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record CelbridgeContext(
        IReadOnlyList<string> AllowedTools,
        IReadOnlyDictionary<string, string> Secrets,
        IReadOnlyDictionary<string, string> Options);
}
