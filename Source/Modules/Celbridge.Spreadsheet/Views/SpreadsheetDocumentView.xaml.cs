using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Spreadsheet.ViewModels;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Spreadsheet.Views;

public sealed partial class SpreadsheetDocumentView : WebViewDocumentView
{
    // Spreadsheets can be large and take significant time to serialize/save.
    private const int SaveRequestTimeoutSeconds = 60;

    // Debounce delay to avoid importing a partially-written file when the backing file changes rapidly.
    private const int ImportDebounceMilliseconds = 300;

    // Retry settings for reading a spreadsheet file that may be mid-write.
    private const int MaxFileReadAttempts = 3;
    private const int FileReadRetryDelayMilliseconds = 150;

    // Magic bytes that identify valid spreadsheet files.
    private static readonly byte[] XlsxMagicBytes = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] XlsMagicBytes = { 0xD0, 0xCF, 0x11, 0xE0 };

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IConfiguration _configuration;

    private SpreadsheetDocumentHandler? _documentHandler;

    private CancellationTokenSource? _importDebounceCts;
    private int _importDebounceVersion;

    public SpreadsheetDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    protected override bool GetDevToolsEnabled()
    {
        return _configuration["AppConfig:Environment"] == "Development";
    }

    public SpreadsheetDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SpreadsheetDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWebViewFactory webViewFactory,
        IFeatureFlags featureFlags)
        : base(messengerService, webViewFactory, featureFlags)
    {
        ViewModel = serviceProvider.GetRequiredService<SpreadsheetDocumentViewModel>();
        _configuration = serviceProvider.GetRequiredService<IConfiguration>();

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

            // Inject SpreadJS license keys before the page loads so they are available
            // as globals when spreadsheet.js runs. The keys are compiled into the binary
            // rather than shipped as a readable file in the app package folder.
            if (!string.IsNullOrEmpty(SpreadsheetLicenseKeys.LicenseKey))
            {
                var licenseScript =
                    $"window.SPREAD_JS_LICENSE_KEY='{SpreadsheetLicenseKeys.LicenseKey}';" +
                    $"window.SPREAD_JS_DESIGNER_LICENSE_KEY='{SpreadsheetLicenseKeys.DesignerLicenseKey}';";
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(licenseScript);
            }

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

            _documentHandler.ContentLoaded += SetContentLoaded;

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

        for (int attempt = 1; attempt <= MaxFileReadAttempts; attempt++)
        {
            try
            {
                byte[] bytes;

                // Open with FileShare.ReadWrite to allow Excel to keep the file open.
                using (var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    bytes = new byte[fileStream.Length];
                    await fileStream.ReadExactlyAsync(bytes, CancellationToken.None);
                }

                // Validate magic bytes to detect a partial write (e.g. Excel still saving).
                if (!HasValidSpreadsheetMagicBytes(bytes))
                {
                    if (attempt < MaxFileReadAttempts)
                    {
                        _logger.LogDebug($"Spreadsheet file has invalid magic bytes on attempt {attempt}, retrying: {filePath}");
                        await Task.Delay(FileReadRetryDelayMilliseconds);
                        continue;
                    }

                    _logger.LogWarning($"Spreadsheet file has invalid magic bytes after {MaxFileReadAttempts} attempts: {filePath}");
                    return string.Empty;
                }

                var base64 = Convert.ToBase64String(bytes);
                _logger.LogDebug($"Successfully loaded spreadsheet as base64: {filePath}");
                return base64;
            }
            catch (IOException ex)
            {
                if (attempt < MaxFileReadAttempts)
                {
                    _logger.LogDebug($"IO error reading spreadsheet on attempt {attempt}, retrying: {filePath}");
                    await Task.Delay(FileReadRetryDelayMilliseconds);
                }
                else
                {
                    _logger.LogError(ex, $"Failed to load spreadsheet after {MaxFileReadAttempts} attempts: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to load spreadsheet: {filePath}");
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool HasValidSpreadsheetMagicBytes(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        var isXlsx = bytes[0] == XlsxMagicBytes[0] &&
                     bytes[1] == XlsxMagicBytes[1] &&
                     bytes[2] == XlsxMagicBytes[2] &&
                     bytes[3] == XlsxMagicBytes[3];

        var isXls = bytes[0] == XlsMagicBytes[0] &&
                    bytes[1] == XlsMagicBytes[1] &&
                    bytes[2] == XlsMagicBytes[2] &&
                    bytes[3] == XlsMagicBytes[3];

        return isXlsx || isXls;
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= SpreadsheetDocumentView_Loaded;

        // Unsubscribe from events
        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;
        if (_documentHandler is not null)
        {
            _documentHandler.ContentLoaded -= SetContentLoaded;
        }

        // Cancel and dispose any pending debounce timer
        _importDebounceCts?.Cancel();
        _importDebounceCts?.Dispose();
        _importDebounceCts = null;

        // Cleanup ViewModel message handlers
        ViewModel.Cleanup();

        await base.PrepareToClose();
    }

    private void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Cancel any in-flight debounce and start a new one.
        _importDebounceCts?.Cancel();
        _importDebounceCts = new CancellationTokenSource();
        var cancellationToken = _importDebounceCts.Token;
        var capturedVersion = ++_importDebounceVersion;

        _ = Task.Delay(ImportDebounceMilliseconds, cancellationToken).ContinueWith(delayTask =>
        {
            if (delayTask.IsCanceled)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                // Guard against the race where the delay completed just before a new change
                // arrived on the UI thread and started a fresh debounce cycle.
                if (capturedVersion != _importDebounceVersion)
                {
                    return;
                }

                if (_documentHandler is not null && _documentHandler.IsImportInProgress)
                {
                    _documentHandler.HasPendingImport = true;
                    _logger.LogDebug("Import already in progress, queuing pending import");
                    return;
                }

                // Set IsImportInProgress immediately so that any further resource change events
                // that arrive before JS calls back into LoadAsync are queued, not fired concurrently.
                if (_documentHandler is not null)
                {
                    _documentHandler.IsImportInProgress = true;
                }

                // Route through the base class orchestration so editor state (zoom, active sheet,
                // selection) is preserved across the reload via the standard onRequestState /
                // onRestoreState flow. Fire-and-forget: the orchestration handles its own errors.
                _ = ReloadWithStatePreservationAsync();
            });
        }, TaskScheduler.Default);
    }
}
