using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Spreadsheet.ViewModels;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Spreadsheet.Views;

public sealed partial class SpreadsheetDocumentView : WebView2DocumentView, IHostDocument
{
    // Spreadsheets can be large and take significant time to serialize/save.
    private const int SaveRequestTimeoutSeconds = 60;

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    // Track import state to prevent race conditions during initial load and reloads
    private bool _isImportInProgress;
    private bool _hasPendingImport;

    // Track save result from async RPC callback
    private TaskCompletionSource<Result>? _saveResultTcs;

    public SpreadsheetDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SpreadsheetDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
        : base(messengerService)
    {
        ViewModel = serviceProvider.GetRequiredService<SpreadsheetDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        this.InitializeComponent();

        // Assign the WebView from XAML to the base class property
        WebView = SpreadsheetWebView;

        Loaded += SpreadsheetDocumentView_Loaded;

        // Subscribe to reload requests from the ViewModel
        ViewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
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

        // Set up completion source to receive the save result from SaveAsync
        _saveResultTcs = new TaskCompletionSource<Result>();

        // Request the JS side to save - it will call document/save
        // which triggers our SaveAsync handler
        await Host.NotifyRequestSaveAsync();

        // Wait for SaveAsync to complete, with timeout to prevent hanging
        var timeout = TimeSpan.FromSeconds(SaveRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_saveResultTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _saveResultTcs = null;
            CompleteSave();

            var errorMessage = $"Spreadsheet editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"The editor may be in an unstable state. File: {ViewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await _saveResultTcs.Task;
        _saveResultTcs = null;

        return result;
    }

    private async void SpreadsheetDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        // Unregister for UI load events.
        // Switching tabs while spreadsheet view is loading triggers a load event.
        Loaded -= SpreadsheetDocumentView_Loaded;

        await InitSpreadsheetViewAsync();
    }

    private async Task InitSpreadsheetViewAsync()
    {
        try
        {
            Guard.IsNotNull(WebView);

            await WebView.EnsureCoreWebView2Async();

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "spreadjs.celbridge",
                "Celbridge.Spreadsheet/Web/SpreadJS",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

            // Inject keyboard shortcut handler for F11 and other global shortcuts
            await WebView2Helper.InjectShortcutHandlerAsync(WebView.CoreWebView2);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Use the system browser if the user clicks on links in the spreadsheet UI.
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                // Allow the initial page load
                if (uri.StartsWith("https://spreadjs.celbridge/index.html"))
                {
                    return;
                }

                args.Cancel = true;
                OpenSystemBrowser(uri);
            };

            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            // Initialize the host
            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host");
                return;
            }

            // Register this view as the handler for additional RPC interfaces
            Host.AddLocalRpcTarget<IHostDocument>(this);

            StartHostListener();

            // Initialize focus handling
            InitializeFocusHandling();

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://spreadjs.celbridge/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Spreadsheet Web View.");
        }
    }

    #region IHostDocument

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        // Validate protocol version
        if (protocolVersion != "1.0")
        {
            throw new LocalRpcException($"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
        }

        // Load spreadsheet as base64 - content is stored in the result
        var base64Content = await LoadSpreadsheetAsBase64Async();

        var metadata = CreateMetadata();

        // Gather localization strings (none needed for spreadsheet currently)
        var localization = new Dictionary<string, string>();

        // Mark import as in progress - JS will notify us when complete
        _isImportInProgress = true;

        // Use content field to pass base64 data for spreadsheet
        return new InitializeResult(base64Content, metadata, localization);
    }

    public async Task<LoadResult> LoadAsync()
    {
        // Mark import as in progress
        _isImportInProgress = true;

        var base64Content = await LoadSpreadsheetAsBase64Async();
        var metadata = CreateMetadata();

        return new LoadResult(base64Content, metadata);
    }

    /// <summary>
    /// Called by JS via RPC when the spreadsheet data needs to be saved.
    /// Saves the data to disk and signals the waiting SaveDocument() method with the result.
    /// </summary>
    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            // Write the spreadsheet data to disk
            var saveResult = await ViewModel.SaveSpreadsheetDataToFile(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save spreadsheet data");
                return CompleteSaveWithResult(saveResult);
            }

            // Reset the ViewModel's save state flags
            await ViewModel.SaveDocument();

            // Check if another save was requested while this one was in progress
            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                ViewModel.OnDataChanged();
            }

            return SignalSaveResult(Result.Ok(), success: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during save");
            var failResult = Result.Fail("Exception during save").WithException(ex);
            return CompleteSaveWithResult(failResult, ex.Message);
        }
    }

    /// <summary>
    /// Completes a failed save operation: signals the waiting SaveDocument() and returns failure to JS.
    /// </summary>
    private SaveResult CompleteSaveWithResult(Result failResult, string? errorMessage = null)
    {
        CompleteSave();
        _saveResultTcs?.TrySetResult(failResult);
        return new SaveResult(false, errorMessage ?? failResult.Error);
    }

    /// <summary>
    /// Signals the waiting SaveDocument() method and returns the result to JS.
    /// </summary>
    private SaveResult SignalSaveResult(Result result, bool success, string? errorMessage = null)
    {
        _saveResultTcs?.TrySetResult(result);
        return new SaveResult(success, errorMessage);
    }

    #endregion

    #region IHostNotifications overrides

    public override void OnDocumentChanged()
    {
        // Flag the document as modified so it will attempt to save after a short delay.
        ViewModel.OnDataChanged();
    }

    public override void OnImportComplete(bool success, string? error = null)
    {
        _isImportInProgress = false;

        if (!success)
        {
            _logger.LogWarning($"Spreadsheet import failed: {error}");
        }
        else
        {
            _logger.LogDebug("Spreadsheet import completed successfully");
        }

        // If another file change occurred while we were importing, we need to import again
        if (_hasPendingImport)
        {
            _logger.LogDebug("Processing pending import request");
            _hasPendingImport = false;
            Host?.NotifyExternalChangeAsync();
        }
    }

    #endregion

    private DocumentMetadata CreateMetadata()
    {
        return new DocumentMetadata(
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));
    }

    private async Task<string> LoadSpreadsheetAsBase64Async()
    {
        var filePath = ViewModel.FilePath;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Cannot load spreadsheet - file does not exist: {filePath}");
            return string.Empty;
        }

        try
        {
            // Open with FileShare.ReadWrite to allow Excel to keep the file open
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            var bytes = new byte[fileStream.Length];
            await fileStream.ReadExactlyAsync(bytes, CancellationToken.None);
            var base64 = Convert.ToBase64String(bytes);

            _logger.LogDebug($"Successfully loaded spreadsheet as base64: {filePath}");
            return base64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load spreadsheet: {filePath}");
            return string.Empty;
        }
    }

    private void OpenSystemBrowser(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (_resourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Result.Fail($"File resource does not exist in resource registry: {fileResource}");
        }

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File resource does not exist on disk: {fileResource}");
        }

        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;

        await Task.CompletedTask;

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        Loaded -= SpreadsheetDocumentView_Loaded;

        // Unsubscribe from ViewModel events
        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        // Cleanup ViewModel message handlers
        ViewModel.Cleanup();

        await base.PrepareToClose();
    }

    private void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // If an import is already in progress, mark that we need another import after this one completes
        if (_isImportInProgress)
        {
            _hasPendingImport = true;
            _logger.LogDebug("Import already in progress, queuing pending import");
            return;
        }

        // Notify JS to reload the spreadsheet from disk
        Host?.NotifyExternalChangeAsync();
    }
}
