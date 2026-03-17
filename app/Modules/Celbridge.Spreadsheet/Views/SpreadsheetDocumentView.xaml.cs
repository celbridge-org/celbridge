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

public sealed partial class SpreadsheetDocumentView : WebViewDocumentView
{
    // Spreadsheets can be large and take significant time to serialize/save.
    private const int SaveRequestTimeoutSeconds = 60;

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

    private SpreadsheetDocumentHandler? _documentHandler;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

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

            var errorMessage = $"Spreadsheet editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"The editor may be in an unstable state. File: {ViewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await saveResultTcs.Task;
        _documentHandler.SaveResultTcs = null;

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

            _documentHandler = new SpreadsheetDocumentHandler(
                ViewModel,
                _logger,
                CreateDocumentMetadata,
                LoadSpreadsheetAsBase64Async,
                CompleteSave,
                () => Host?.NotifyExternalChangeAsync());

            Host.AddLocalRpcTarget<IHostDocument>(_documentHandler);

            StartHostListener();

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://spreadjs.celbridge/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Spreadsheet Web View.");
        }
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
        if (_documentHandler is not null && _documentHandler.IsImportInProgress)
        {
            _documentHandler.HasPendingImport = true;
            _logger.LogDebug("Import already in progress, queuing pending import");
            return;
        }

        Host?.NotifyExternalChangeAsync();
    }
}
