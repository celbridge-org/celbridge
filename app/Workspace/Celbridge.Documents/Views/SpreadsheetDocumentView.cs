using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Path = System.IO.Path;

namespace Celbridge.Documents.Views;

public sealed partial class SpreadsheetDocumentView : DocumentView
{
    private ILogger _logger;
    private IStringLocalizer _stringLocalizer;
    private ICommandService _commandService;
    private IDialogService _dialogService;
    private IResourceRegistry _resourceRegistry;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    // Track save state to prevent race conditions
    private bool _isSaveInProgress = false;
    private bool _hasPendingSave = false;

    // Track import state to prevent race conditions during initial load and reloads
    private bool _isImportInProgress = false;
    private bool _hasPendingImport = false;

    public SpreadsheetDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SpreadsheetDocumentView> logger,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<SpreadsheetDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        Loaded += SpreadsheetDocumentView_Loaded;

        // Subscribe to reload requests from the ViewModel
        ViewModel.ReloadRequested += ViewModel_ReloadRequested;

        //
        // Set the data context
        // 

        this.DataContext(ViewModel);
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        Guard.IsNotNull(_webView);

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
        _webView.CoreWebView2.PostWebMessageAsString("request_save");

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
            var webView = new WebView2();
            await webView.EnsureCoreWebView2Async();

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("spreadjs.celbridge", 
                "Celbridge.Documents/Web/SpreadJS", 
                CoreWebView2HostResourceAccessKind.Allow);

            // This fixes a visual bug where the WebView2 control would show a white background briefly when
            // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
            webView.DefaultBackgroundColor = Colors.Transparent;

            try
            {
                // The SpreadJS Excel editor is only available in Celbridge installer builds.
                // Check if the SpreadJS license file exists in the app package.
                var uri = new Uri("ms-appx:///Celbridge.Documents/Web/SpreadJS/lib/license.js");
                _ = await StorageFile.GetFileFromApplicationUriAsync(uri);
            }
            catch (Exception)
            {
                // The SpreadJS license file is not present, display an error message and exit.
                webView.CoreWebView2.Navigate("https://spreadjs.celbridge/error.html");
                _webView = webView;
                this.Content = _webView;
                return;
            }

            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            webView.CoreWebView2.Navigate("https://spreadjs.celbridge/index.html");

            bool isEditorReady = false;
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

            webView.WebMessageReceived += onWebMessageReceived;

            while (!isEditorReady)
            {
                await Task.Delay(50);
            }

            webView.WebMessageReceived -= onWebMessageReceived;

            // Use the system browser if the user clicks on links in the spreadsheet UI.

            webView.NavigationStarting += (s, args) =>
            {
                args.Cancel = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            webView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            // Fixes a visual bug where the WebView2 control would show a white background briefly when
            // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
            webView.DefaultBackgroundColor = Colors.Transparent;

            _webView = webView;

            var filePath = ViewModel.FilePath;
            await LoadSpreadsheet(filePath);

            this.Content = _webView;
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
        Guard.IsNotNull(_webView);

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

            byte[] bytes = new byte[fileStream.Length];
            // Use ReadExactlyAsync to ensure all bytes are read (fixes CA2022 warning)
            await fileStream.ReadExactlyAsync(bytes, CancellationToken.None);
            string base64 = Convert.ToBase64String(bytes);

            _webView.CoreWebView2.PostWebMessageAsString(base64);

            // Ensure event handler is registered
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.WebMessageReceived += WebView_WebMessageReceived;

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

    private async void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var webMessage = args.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(webMessage))
        {
            _logger.LogError("Invalid web message received");
            return;
        }

        if (webMessage == "toggle_layout")
        {
            _commandService.Execute<ISetLayoutCommand>(command =>
            { 
                command.Transition = LayoutTransition.ToggleLayout; 
            });
            return;
        }
        else if (webMessage == "load_excel_data")
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

        if (_webView != null)
        {
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            
            // Note: Event handlers were added with lambda functions, so we can't unregister them
            // individually but the Close() method will clean them up anyway.

            _webView.Close();
            _webView = null;
        }

        await base.PrepareToClose();
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Reload the spreadsheet from disk when an external change is detected
        if (_webView != null)
        {
            var filePath = ViewModel.FilePath;
            await LoadSpreadsheet(filePath);
        }
    }
}
