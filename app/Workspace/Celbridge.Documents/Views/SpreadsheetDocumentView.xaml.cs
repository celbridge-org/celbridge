using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

public sealed partial class SpreadsheetDocumentView : WebView2DocumentView
{
    private readonly ILogger _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IResourceRegistry _resourceRegistry;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    // Track save state to prevent race conditions
    private bool _isSaveInProgress;
    private bool _hasPendingSave;

    // Track import state to prevent race conditions during initial load and reloads
    private bool _isImportInProgress;
    private bool _hasPendingImport;

    public SpreadsheetDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SpreadsheetDocumentView> logger,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
        : base(messengerService)
    {
        ViewModel = serviceProvider.GetRequiredService<SpreadsheetDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
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
        Guard.IsNotNull(WebView);

        // If a save is already in progress, mark that we need another save after this one completes
        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;

        // Send a message to request the data to be serialized and sent back as another message.
        WebView.CoreWebView2.PostWebMessageAsString("request_save");

        return await ViewModel.SaveDocument();
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
                "Celbridge.Documents/Web/SpreadJS",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            WebView.CoreWebView2.Navigate("https://spreadjs.celbridge/index.html");

            var isEditorReady = false;
            TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
            {
                var message = e.TryGetWebMessageAsString();

                if (message == "editor_ready")
                {
                    isEditorReady = true;
                    return;
                }

                throw new InvalidOperationException($"Expected 'editor_ready' message, but received: {message}");
            };

            WebView.WebMessageReceived += onWebMessageReceived;

            while (!isEditorReady)
            {
                await Task.Delay(50);
            }

            WebView.WebMessageReceived -= onWebMessageReceived;

            // Initialize base WebView2 functionality (keyboard shortcuts, focus handling)
            // Must be done AFTER editor-ready to avoid the base handler receiving initialization messages
            await InitializeWebViewAsync();

            // Use the system browser if the user clicks on links in the spreadsheet UI.
            WebView.NavigationStarting += (s, args) =>
            {
                args.Cancel = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            var filePath = ViewModel.FilePath;
            await LoadSpreadsheet(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Spreadsheet Web View.");
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

    private async Task LoadSpreadsheet(string filePath)
    {
        Guard.IsNotNull(WebView);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Cannot load spreadsheet - file does not exist: {filePath}");
            return;
        }

        // If an import is already in progress, mark that we need another import after this one completes
        if (_isImportInProgress)
        {
            _hasPendingImport = true;
            _logger.LogDebug("Import already in progress, queuing pending import");
            return;
        }

        _isImportInProgress = true;
        _hasPendingImport = false;

        try
        {
            // Open with FileShare.ReadWrite to allow Excel to keep the file open
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            var bytes = new byte[fileStream.Length];
            // Use ReadExactlyAsync to ensure all bytes are read (fixes CA2022 warning)
            await fileStream.ReadExactlyAsync(bytes, CancellationToken.None);
            var base64 = Convert.ToBase64String(bytes);

            WebView.CoreWebView2.PostWebMessageAsString(base64);

            _logger.LogDebug($"Successfully sent spreadsheet data to SpreadJS for import: {filePath}");
        }
        catch (Exception ex)
        {
            // Log the error but don't crash - the spreadsheet stays in its current state
            _logger.LogError(ex, $"Failed to load spreadsheet: {filePath}");

            // Clear the import flag so we can try again
            _isImportInProgress = false;
        }
    }

    protected override async void OnWebMessageReceived(string? webMessage)
    {
        if (string.IsNullOrEmpty(webMessage))
        {
            _logger.LogError("Invalid web message received");
            return;
        }

        // Try to handle as a global keyboard shortcut first
        base.OnWebMessageReceived(webMessage);

        if (webMessage == "load_excel_data")
        {
            // This will discard any pending changes so ask user to confirm
            var title = _stringLocalizer.GetString("Documents_LoadDocumentConfirmTitle");
            var filename = Path.GetFileName(ViewModel.FilePath);
            var message = _stringLocalizer.GetString("Documents_LoadDocumentConfirm", filename);
            var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message);

            var confirmed = confirmResult.Value;

            if (confirmed)
            {
                var filePath = ViewModel.FilePath;
                await LoadSpreadsheet(filePath);
            }
            return;
        }
        else if (webMessage == "data_changed")
        {
            // Flag the document as modified so it will attempt to save after a short delay.
            ViewModel.OnDataChanged();
            return;
        }
        else if (webMessage == "import_complete")
        {
            // Import is complete (initial load or reload)
            _isImportInProgress = false;

            // If another file change occurred while we were importing, we need to import again
            if (_hasPendingImport)
            {
                _logger.LogDebug("Processing pending import request");
                _hasPendingImport = false;
                var filePath = ViewModel.FilePath;
                await LoadSpreadsheet(filePath);
            }
            return;
        }

        // Any other message is assumed to be base 64 encoded data to avoid string processing.
        // This data was sent from the JS side in response to a "request_save" message.
        var spreadsheetData = webMessage;
        await SaveSpreadsheet(spreadsheetData);
    }

    private async Task SaveSpreadsheet(string spreadsheetData)
    {
        var saveResult = await ViewModel.SaveSpreadsheetDataToFile(spreadsheetData);

        if (saveResult.IsFailure)
        {
            _logger.LogError(saveResult, "Failed to save spreadsheet data");

            // Alert the user that the document failed to save
            var file = ViewModel.FilePath;
            var title = _stringLocalizer.GetString("Documents_SaveDocumentFailedTitle");
            var message = _stringLocalizer.GetString("Documents_SaveDocumentFailedGeneric", file);
            await _dialogService.ShowAlertDialogAsync(title, message);
        }

        // Save is complete, clear the flag
        _isSaveInProgress = false;

        // If changes occurred during the save, trigger another save
        if (_hasPendingSave)
        {
            _logger.LogDebug("Processing pending save request");
            _hasPendingSave = false;
            ViewModel.OnDataChanged(); // Re-trigger a pending save
        }
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

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Reload the spreadsheet from disk when an external change is detected
        if (WebView is not null)
        {
            var filePath = ViewModel.FilePath;
            await LoadSpreadsheet(filePath);
        }
    }
}
