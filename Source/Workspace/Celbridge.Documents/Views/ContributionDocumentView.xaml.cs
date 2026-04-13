using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for custom WebView-based contribution editors.
/// Configured from a DocumentContribution, delegates RPC handling to handler classes.
/// </summary>
public sealed partial class ContributionDocumentView : WebViewDocumentView
{
    private const int SaveRequestTimeoutSeconds = 30;

    private readonly ILogger<ContributionDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    private readonly ContributionDocumentViewModel _viewModel;

    private ContributionDocumentHandler? _documentHandler;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The document contribution that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public CustomDocumentContribution? Contribution { get; set; }

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

        _viewModel = serviceProvider.GetRequiredService<ContributionDocumentViewModel>();

        this.InitializeComponent();

        WebViewContainer = ContributionWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

        Loaded += ContributionDocumentView_Loaded;

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

    private async void ContributionDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ContributionDocumentView_Loaded;

        await InitContributionViewAsync();
    }

    private async Task InitContributionViewAsync()
    {
        if (Contribution is null)
        {
            _logger.LogError("Cannot initialize contribution view: Contribution is not set");
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

            var dialogHandler = new ContributionDialogHandler(
                _dialogService,
                _stringLocalizer,
                _viewModel);

            Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
            Host.AddLocalRpcTarget<IHostDialog>(dialogHandler);

            StartHostListener();

            // Navigate to the contribution's entry point
            var entryPoint = Contribution.EntryPoint;
            var entryUrl = $"https://{Contribution.Package.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize contribution view: {Contribution.Package.Name}");
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
        await Task.CompletedTask;
        return Result.Ok();
    }

    public override async Task<string?> SaveEditorStateAsync()
    {
        if (Host is null)
        {
            return null;
        }

        try
        {
            return await Host.RequestStateAsync();
        }
        catch
        {
            // Editor doesn't implement state saving — that's fine
            return null;
        }
    }

    public override async Task RestoreEditorStateAsync(string state)
    {
        if (Host is null)
        {
            return;
        }

        try
        {
            await Host.NotifyRestoreStateAsync(state);
        }
        catch
        {
            // Editor doesn't implement state restoration — that's fine
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= ContributionDocumentView_Loaded;

        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;

        _viewModel.Cleanup();

        await base.PrepareToClose();
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        if (Host is not null)
        {
            await Host.NotifyExternalChangeAsync();
        }
    }
}
