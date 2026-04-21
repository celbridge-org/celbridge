using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Secrets;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for custom WebView-based contribution editors.
/// Configured from a DocumentEditorContribution, delegates RPC handling to handler classes.
/// </summary>
public sealed partial class ContributionDocumentView : WebViewDocumentView
{
    private const int SaveRequestTimeoutSeconds = 30;
    private const int LoadContentTimeoutSeconds = 10;

    private readonly ILogger<ContributionDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;

    private readonly ContributionDocumentViewModel _viewModel;

    private ContributionDocumentHandler? _documentHandler;
    private ContributionToolsHandler? _toolsHandler;

    // Completed by InitContributionViewAsync with the init outcome. LoadContent
    // triggers the init on first call and awaits this TCS so the open-document
    // flow returns only when the WebView and host are ready for RPCs.
    private TaskCompletionSource<Result>? _initTcs;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The document contribution that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public CustomDocumentEditorContribution? Contribution { get; set; }

    protected override bool GetDevToolsEnabled()
    {
        return base.GetDevToolsEnabled() && (Contribution?.DevToolsEnabled ?? true);
    }

    public ContributionDocumentView(
        IServiceProvider serviceProvider,
        ILogger<ContributionDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWebViewFactory webViewFactory,
        IFeatureFlags featureFlags)
        : base(messengerService, webViewFactory, featureFlags)
    {
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;

        _viewModel = serviceProvider.GetRequiredService<ContributionDocumentViewModel>();

        this.InitializeComponent();

        WebViewContainer = ContributionWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

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

    private async Task InitContributionViewAsync()
    {
        if (Contribution is null)
        {
            var error = "Cannot initialize contribution view: Contribution is not set";
            _logger.LogError(error);
            _initTcs!.TrySetResult(Result.Fail(error));
            return;
        }

        // Pass the contribution to the ViewModel for template content loading
        _viewModel.Contribution = Contribution;

        try
        {
            await AcquireWebViewAsync();

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

            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host for contribution");
                return;
            }

            _documentHandler = new ContributionDocumentHandler(
                _viewModel,
                _logger,
                CreateDocumentMetadata,
                CompleteSave);

            _documentHandler.ContentLoaded += SetContentLoaded;

            var dialogHandler = new ContributionDialogHandler(
                _dialogService,
                _stringLocalizer,
                _viewModel);

            Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
            Host.AddLocalRpcTarget<IHostDialog>(dialogHandler);

            var toolBridge = _serviceProvider.GetService<IMcpToolBridge>();
            if (toolBridge is not null)
            {
                _toolsHandler = new ContributionToolsHandler(toolBridge, Contribution.Package.RequiresTools);
                Host.AddLocalRpcTarget<ContributionToolsHandler>(_toolsHandler);
            }

            StartHostListener();

            // Navigate to the contribution's entry point
            var entryPoint = Contribution.EntryPoint;
            var entryUrl = $"https://{Contribution.Package.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);

            _initTcs!.TrySetResult(Result.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize contribution view: {Contribution.Package.Name}");
            var failure = Result.Fail($"Failed to initialize contribution view: {Contribution.Package.Id}")
                .WithException(ex);
            _initTcs!.TrySetResult(failure);
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
            // External URL
            OpenSystemBrowser(_commandService, href);
        }
        else
        {
            // Internal resource
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
            });
        }
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
        // Loaded-driven init would not fire in time for LoadContent to
        // observe the editor's ContentLoaded signal. LoadContent returns
        // only once the editor is ready for RPCs, so callers that open a
        // document and immediately send it an RPC (ApplyEditsCommand,
        // NavigateToLocation, etc.) find the host ready to dispatch.
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

        if (IsContentLoaded)
        {
            return Result.Ok();
        }

        var readyTcs = new TaskCompletionSource();
        void OnContentLoadedHandler(ContentLoadedReason reason)
        {
            if (reason == ContentLoadedReason.Initial)
            {
                readyTcs.TrySetResult();
            }
        }

        ContentLoaded += OnContentLoadedHandler;
        try
        {
            // Double-check after subscribing to avoid a race where the load
            // completes between the first check and the subscription.
            if (IsContentLoaded)
            {
                return Result.Ok();
            }

            var timeout = TimeSpan.FromSeconds(LoadContentTimeoutSeconds);
            var completed = await Task.WhenAny(readyTcs.Task, Task.Delay(timeout));
            if (completed != readyTcs.Task)
            {
                var errorMessage = $"Contribution document failed to load within {LoadContentTimeoutSeconds} seconds. " +
                                   $"Package: {Contribution?.Package.Id}. File: {_viewModel.FilePath}";
                _logger.LogError(errorMessage);
                return Result.Fail(errorMessage);
            }

            return Result.Ok();
        }
        finally
        {
            ContentLoaded -= OnContentLoadedHandler;
        }
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

    public override async Task<Result> ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        if (Host is null)
        {
            return Result.Fail("Host not initialized");
        }

        try
        {
            var wireEdits = edits.Select(edit => new
            {
                line = edit.Line,
                column = edit.Column,
                endLine = edit.EndLine,
                endColumn = edit.EndColumn,
                newText = edit.NewText
            });

            await Host.Rpc.NotifyWithParameterObjectAsync(
                "editor/applyEdits",
                new { edits = wireEdits });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to apply edits to document")
                .WithException(ex);
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        if (_documentHandler is not null)
        {
            _documentHandler.ContentLoaded -= SetContentLoaded;
        }

        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;

        _viewModel.Cleanup();

        await base.PrepareToClose();
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
        var secrets = ResolvePackageSecrets(package);

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

    private IReadOnlyDictionary<string, string> ResolvePackageSecrets(PackageInfo package)
    {
        if (package.RequiresSecrets.Count == 0)
        {
            return EmptySecrets;
        }

        // Gate: only bundled packages (shipped inside module DLLs) can request secrets.
        // Project-contributed and registry-installed packages would otherwise be able to
        // exfiltrate any secret whose name they know (e.g. `spreadjs_license` is visible
        // in public source). When a legitimate third-party use case appears, replace this
        // blanket block with a per-secret consent prompt persisted per package-id.
        if (!package.IsBundled)
        {
            _logger.LogWarning(
                "Non-bundled package '{PackageId}' declares {Count} required secret(s); " +
                "secret injection is only permitted for bundled packages. Secrets: {Secrets}",
                package.Id, package.RequiresSecrets.Count, string.Join(", ", package.RequiresSecrets));
            return EmptySecrets;
        }

        var secretRegistry = _serviceProvider.GetService<ISecretRegistry>();
        if (secretRegistry is null)
        {
            _logger.LogWarning(
                "Package '{PackageId}' declares {Count} required secret(s) but no ISecretRegistry is registered",
                package.Id, package.RequiresSecrets.Count);
            return EmptySecrets;
        }

        var resolveResult = secretRegistry.ResolveAll(package.RequiresSecrets);
        if (resolveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to resolve secrets for package '{PackageId}': {Error}",
                package.Id, resolveResult.FirstErrorMessage);
            return EmptySecrets;
        }

        return resolveResult.Value;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptySecrets =
        new Dictionary<string, string>();

    private static readonly JsonSerializerOptions ContextSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record CelbridgeContext(
        IReadOnlyList<string> AllowedTools,
        IReadOnlyDictionary<string, string> Secrets,
        IReadOnlyDictionary<string, string> Options);
}
