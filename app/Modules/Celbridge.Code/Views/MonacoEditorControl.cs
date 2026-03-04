using Celbridge.Code.MonacoHost;
using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Code.Views;

/// <summary>
/// A reusable control that hosts a Monaco code editor via WebView2.
/// This is a pure text editing control that can be embedded in any document view.
/// The parent view is responsible for file I/O and document management.
/// </summary>
public sealed partial class MonacoEditorControl : UserControl, IHostDocument, IHostNotifications
{
    private const int ContentRequestTimeoutSeconds = 5;

    private readonly ILogger<MonacoEditorControl> _logger;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ICommandService _commandService;

    private WebView2? _webView;
    private JsonRpc? _rpc;
    private HostRpcHandler? _rpcHandler;
    private HostChannel? _messageChannel;
    private TaskCompletionSource? _clientReadyTcs;
    private TaskCompletionSource<string>? _getContentTcs;

    private string _content = string.Empty;
    private string _language = "plaintext";
    private string _filePath = string.Empty;
    private string _resourceKey = string.Empty;

    /// <summary>
    /// Raised when the content changes in the Monaco editor (user editing).
    /// </summary>
    public event Action? ContentChanged;

    /// <summary>
    /// Raised when an external reload is requested (e.g., file changed on disk).
    /// </summary>
    public event EventHandler? ReloadRequested;

    /// <summary>
    /// Raised when the editor receives focus.
    /// </summary>
    public event Action? EditorFocused;

    public MonacoEditorControl()
    {
        _logger = ServiceLocator.AcquireService<ILogger<MonacoEditorControl>>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        // Monitor theme changes to update Monaco editor theme
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    /// <summary>
    /// Initializes the Monaco editor with content and language.
    /// Call this after the control is added to the visual tree.
    /// </summary>
    public async Task<Result> InitializeAsync(string content, string language, string filePath, string resourceKey)
    {
        _content = content;
        _language = language;
        _filePath = filePath;
        _resourceKey = resourceKey;

        _webView = await _webViewFactory.AcquireAsync();

        this.Content(_webView);

        // Set up virtual host mapping for Monaco editor assets
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "monaco.celbridge",
            "Celbridge.Code/Web/Monaco",
            CoreWebView2HostResourceAccessKind.Allow);

        // Map shared assets so Monaco can access celbridge-api.js
        WebView2Helper.MapSharedAssets(_webView.CoreWebView2);

        // Inject keyboard shortcut handler for F11 and other global shortcuts
        await WebView2Helper.InjectShortcutHandlerAsync(_webView.CoreWebView2);

        // Ensure we only register the event handlers once
        _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        _webView.GotFocus -= WebView_GotFocus;

        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.GotFocus += WebView_GotFocus;

        // Initialize StreamJsonRpc with this control as the handler
        _messageChannel = new HostChannel(_webView.CoreWebView2);
        _rpcHandler = new HostRpcHandler(_messageChannel);
        _rpc = new JsonRpc(_rpcHandler);

        // Ensure RPC method handlers run on the UI thread
        _rpc.SynchronizationContext = SynchronizationContext.Current;

        // Register this control as the handler for RPC interfaces
        _rpc.AddLocalRpcTarget<IHostDocument>(this, null);
        _rpc.AddLocalRpcTarget<IHostNotifications>(this, null);

        _rpc.StartListening();

        // Sync WebView2 color scheme with the app theme
        ApplyThemeToWebView();

        // Prepare to wait for client ready notification
        _clientReadyTcs = new TaskCompletionSource();

        // Navigate to Monaco editor
        _webView.CoreWebView2.Navigate("http://monaco.celbridge/index.html");

        // Wait for the JS client to signal it's ready
        await _clientReadyTcs.Task;

        // Initialize the Monaco editor via JSON-RPC
        await _rpc.NotifyEditorInitializeAsync(_language);

        return Result.Ok();
    }

    /// <summary>
    /// Gets the current content from the Monaco editor.
    /// This requests the content via RPC and waits for the response.
    /// Throws an exception if the Monaco editor fails to respond within the timeout period.
    /// </summary>
    public async Task<string> GetContentAsync()
    {
        if (_rpc is null)
        {
            return _content;
        }

        // Set up completion source to receive the content
        _getContentTcs = new TaskCompletionSource<string>();

        // Request content from Monaco - it will call SaveAsync with the content
        await _rpc.NotifyRequestSaveAsync();

        // Wait for SaveAsync to be called, with timeout to prevent hanging forever
        var timeout = TimeSpan.FromSeconds(ContentRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_getContentTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _getContentTcs = null;

            var errorMessage = $"Monaco editor failed to respond within {ContentRequestTimeoutSeconds} seconds. " +
                               $"The editor may be in an unstable state. File: {_filePath}";

            _logger.LogError(errorMessage);

            throw new TimeoutException(errorMessage);
        }

        var content = await _getContentTcs.Task;
        _getContentTcs = null;

        return content;
    }

    /// <summary>
    /// Sets the content in the Monaco editor.
    /// </summary>
    public void SetContent(string content)
    {
        _content = content;
    }

    /// <summary>
    /// Sets the language mode of the Monaco editor.
    /// </summary>
    public async Task SetLanguageAsync(string language)
    {
        _language = language;

        if (_rpc is not null)
        {
            await _rpc.NotifyEditorSetLanguageAsync(language);
        }
    }

    /// <summary>
    /// Updates the file metadata (for display and after rename operations).
    /// </summary>
    public async Task UpdateFileInfoAsync(string filePath, string resourceKey, string? newLanguage = null)
    {
        _filePath = filePath;
        _resourceKey = resourceKey;

        if (newLanguage is not null)
        {
            await SetLanguageAsync(newLanguage);
        }
    }

    /// <summary>
    /// Navigates to a specific location in the editor.
    /// </summary>
    public async Task<Result> NavigateToLocationAsync(int lineNumber, int column)
    {
        if (_rpc is null)
        {
            return Result.Fail("RPC is not initialized");
        }

        try
        {
            await _rpc.NotifyEditorNavigateToLocationAsync(lineNumber, column);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: line {lineNumber}, column {column}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Notifies the Monaco editor that the file has changed externally and should reload.
    /// </summary>
    public void NotifyExternalChange()
    {
        _rpc?.NotifyExternalChangeAsync();
    }

    /// <summary>
    /// Prepares the control for disposal.
    /// </summary>
    public async Task CleanupAsync()
    {
        _messengerService.UnregisterAll(this);

        if (_webView == null)
        {
            return;
        }

        _webView.GotFocus -= WebView_GotFocus;

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        }

        // Dispose RPC and detach the message channel
        _rpc?.Dispose();
        _rpcHandler?.Dispose();
        _messageChannel?.Detach();
        _rpc = null;
        _rpcHandler = null;
        _messageChannel = null;

        // Close and dispose the WebView2 instance
        _webView.Close();
        _webView = null;

        await Task.CompletedTask;
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Prevent the new window from being created
        args.Handled = true;

        // Open the URL in the default system browser
        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            _commandService.Execute<IOpenBrowserCommand>(command =>
            {
                command.URL = url;
            });
        }
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        EditorFocused?.Invoke();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (_webView?.CoreWebView2 is not null)
        {
            ApplyThemeToWebView();
        }
    }

    private void ApplyThemeToWebView()
    {
        Guard.IsNotNull(_webView);

        // Use WebView2's PreferredColorScheme API - Monaco JS listens for prefers-color-scheme changes
        var theme = _userInterfaceService.UserInterfaceTheme;
        _webView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    private DocumentMetadata CreateMetadata()
    {
        return new DocumentMetadata(
            _filePath,
            _resourceKey,
            Path.GetFileName(_filePath));
    }

    #region IHostDocument

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        // Validate protocol version
        if (protocolVersion != "1.0")
        {
            throw new HostRpcException(
                JsonRpcErrorCodes.InvalidVersion,
                $"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
        }

        // Build metadata
        var metadata = CreateMetadata();

        // No localization strings needed for Monaco
        var localization = new Dictionary<string, string>();

        var result = new InitializeResult(_content, metadata, localization);

        await Task.CompletedTask;

        return result;
    }

    public async Task<LoadResult> LoadAsync()
    {
        // Raise event so the parent can reload content from disk
        ReloadRequested?.Invoke(this, EventArgs.Empty);

        var metadata = CreateMetadata();
        var result = new LoadResult(_content, metadata);

        await Task.CompletedTask;

        return result;
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        // Update cached content
        _content = content;

        // If we're waiting for content (GetContentAsync was called), complete the task
        _getContentTcs?.TrySetResult(content);

        await Task.CompletedTask;

        // Return success - the actual file save is handled by the parent
        return new SaveResult(true);
    }

    #endregion

    #region IHostNotifications

    public void OnDocumentChanged()
    {
        // Notify parent that content has changed
        ContentChanged?.Invoke();
    }

    public void OnLinkClicked(string href)
    {
        // Link clicks are not used by the Monaco editor
    }

    public void OnImportComplete(bool success, string? error = null)
    {
        // Import completion is not used by the Monaco editor
    }

    public void OnClientReady()
    {
        // Signal that the JS client is ready
        _clientReadyTcs?.TrySetResult();
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    #endregion
}
