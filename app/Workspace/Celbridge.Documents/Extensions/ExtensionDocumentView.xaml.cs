using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Host.Helpers;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// Document view for custom (WebView2-based) extension editors.
/// Configured from an ExtensionManifest, handles the IHostDocument protocol,
/// and inherits SetFileResource, theme syncing, CreateMetadata, and save tracking from the base.
/// </summary>
public sealed partial class ExtensionDocumentView : WebViewDocumentView, IHostDocument
{
    private const int SaveRequestTimeoutSeconds = 30;

    private readonly ILogger<ExtensionDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    private readonly ExtensionDocumentViewModel _viewModel;

    // Track save result from async RPC callback
    private TaskCompletionSource<Result>? _saveResultTcs;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The extension manifest that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public ExtensionManifest? Manifest { get; set; }

    public ExtensionDocumentView(
        IServiceProvider serviceProvider,
        ILogger<ExtensionDocumentView> logger,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IStringLocalizer stringLocalizer,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        _logger = logger;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;

        _viewModel = serviceProvider.GetRequiredService<ExtensionDocumentViewModel>();

        this.InitializeComponent();

        WebViewContainer = ExtensionWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

        Loaded += ExtensionDocumentView_Loaded;

        _viewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        if (Host is null)
        {
            _logger.LogDebug("Save skipped - Host not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _saveResultTcs = new TaskCompletionSource<Result>();

        await Host.NotifyRequestSaveAsync();

        var timeout = TimeSpan.FromSeconds(SaveRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_saveResultTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _saveResultTcs = null;
            CompleteSave();

            var errorMessage = $"Extension editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"File: {_viewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await _saveResultTcs.Task;
        _saveResultTcs = null;

        return result;
    }

    private async void ExtensionDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ExtensionDocumentView_Loaded;

        await InitExtensionViewAsync();
    }

    private async Task InitExtensionViewAsync()
    {
        if (Manifest is null)
        {
            _logger.LogError("Cannot initialize extension view: Manifest is not set");
            return;
        }

        try
        {
            await AcquireWebViewAsync();

            // Map the extension's asset directory to a virtual host
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                Manifest.HostName,
                Manifest.ExtensionDirectory,
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

            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host for extension");
                return;
            }

            Host.AddLocalRpcTarget<IHostDocument>(this);

            StartHostListener();

            // Navigate to the extension's entry point
            var entryPoint = Manifest.EntryPoint ?? "index.html";
            var entryUrl = $"https://{Manifest.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize extension view: {Manifest.Name}");
        }
    }

    #region IHostDocument

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        var content = await _viewModel.LoadTextContentAsync();
        var metadata = CreateDocumentMetadata();

        var localization = WebViewLocalizationHelper.GetLocalizedStrings(
            _stringLocalizer,
            $"Ext_{Manifest?.Name?.Replace(" ", "")}_");

        return new InitializeResult(content, metadata, localization);
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await _viewModel.LoadTextContentAsync();
        var metadata = CreateDocumentMetadata();

        return new LoadResult(content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await _viewModel.SaveTextContentAsync(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save extension document");
                CompleteSave();
                _saveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.Error);
            }

            _viewModel.OnSaveCompleted();

            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                _viewModel.OnDataChanged();
            }

            _saveResultTcs?.TrySetResult(Result.Ok());
            return new SaveResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during extension save");
            CompleteSave();
            var failResult = Result.Fail("Exception during save").WithException(ex);
            _saveResultTcs?.TrySetResult(failResult);
            return new SaveResult(false, ex.Message);
        }
    }

    public void OnDocumentChanged()
    {
        _viewModel.OnDataChanged();
    }

    #endregion

    public override async Task<Result> LoadContent()
    {
        _viewModel.InitializeFileTracking();
        await Task.CompletedTask;
        return Result.Ok();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= ExtensionDocumentView_Loaded;

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
