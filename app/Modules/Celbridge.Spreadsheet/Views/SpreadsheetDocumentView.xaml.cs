using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Spreadsheet.ViewModels;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Spreadsheet.Views;

public sealed partial class SpreadsheetDocumentView : WebViewDocumentView, IHostDocument
{
    // Spreadsheets can be large and take significant time to serialize/save.
    private const int SaveRequestTimeoutSeconds = 60;

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

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
        IUserInterfaceService userInterfaceService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        ViewModel = serviceProvider.GetRequiredService<SpreadsheetDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = SpreadsheetWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

        Loaded += SpreadsheetDocumentView_Loaded;

        // Subscribe to reload requests from the ViewModel
        ViewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
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
            // Acquire WebView from factory and add to container
            await AcquireWebViewAsync();

            // Sync WebView2 color scheme with the app theme
            ApplyThemeToWebView();

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "spreadjs.celbridge",
                "Celbridge.Spreadsheet/Web/SpreadJS",
                CoreWebView2HostResourceAccessKind.Allow);

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
                OpenSystemBrowser(_commandService, uri);
            };

            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(_commandService, uri);
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
            DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

            // Load spreadsheet as base64 - content is stored in the result
            var base64Content = await LoadSpreadsheetAsBase64Async();

            var metadata = CreateDocumentMetadata();

            // Mark import as in progress - JS will notify us when complete
            _isImportInProgress = true;

            // Use content field to pass base64 data for spreadsheet
            return new InitializeResult(base64Content, metadata);
        }

    public async Task<LoadResult> LoadAsync()
    {
        // Mark import as in progress
        _isImportInProgress = true;

        var base64Content = await LoadSpreadsheetAsBase64Async();
        var metadata = CreateDocumentMetadata();

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

    #region IHostDocument

    public void OnDocumentChanged()
    {
        // Flag the document as modified so it will attempt to save after a short delay.
        ViewModel.OnDataChanged();
    }

    public void OnImportComplete(bool success, string? error = null)
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

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

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
