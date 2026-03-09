using Celbridge.Code.Services;
using Celbridge.Commands;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Code.Views;

/// <summary>
/// A reusable control that hosts a code editor via WebView2.
/// This is a pure text editing control that can be embedded in any document view.
/// The parent view is responsible for file I/O and document management.
/// </summary>
public sealed partial class CodeEditor : UserControl, IHostDocument, IHostInput
{
    private const int ContentRequestTimeoutSeconds = 5;
    private const int ClientInitializationTimeoutSeconds = 10;

    private readonly ILogger<CodeEditor> _logger;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ICommandService _commandService;

    private WebView2? _webView;
    private CodeEditorHost? _host;
    private WebViewHostChannel? _messageChannel;
    private TaskCompletionSource? _clientReadyTcs;
    private TaskCompletionSource<string>? _getContentTcs;

    private bool _isPreInitialized;
    private string _content = string.Empty;
    private string _language = "plaintext";
    private string _filePath = string.Empty;
    private string _resourceKey = string.Empty;
    private CodeEditorOptions _options = CodeEditorOptions.Default;

    /// <summary>
    /// Callback to load content from the parent. Set this before calling InitializeAsync().
    /// The parent is responsible for reading from disk or other source.
    /// </summary>
    public Func<Task<string>>? ContentLoader { get; set; }

    /// <summary>
    /// Editor options. Set this before calling InitializeAsync() or PreInitializeAsync().
    /// </summary>
    public CodeEditorOptions Options
    {
        get => _options;
        set => _options = value;
    }

    /// <summary>
    /// Raised when the content changes in the Monaco editor (user editing).
    /// </summary>
    public event Action? ContentChanged;

    /// <summary>
    /// Raised when the editor receives focus.
    /// </summary>
    public event Action? EditorFocused;

    /// <summary>
    /// Raised when the scroll position changes in the Monaco editor.
    /// The parameter is the scroll percentage (0.0 to 1.0).
    /// </summary>
    public event Action<double>? ScrollPositionChanged;

    public CodeEditor()
    {
        _logger = ServiceLocator.AcquireService<ILogger<CodeEditor>>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        // Monitor theme changes to update Monaco editor theme
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    /// <summary>
    /// Pre-initializes the Monaco editor by setting up WebView2 and waiting for the client to be ready.
    /// This performs the expensive initialization steps without loading any content.
    /// Call InitializeAsync() later to load actual content.
    /// </summary>
    public async Task<Result> PreInitializeAsync()
    {
        if (_isPreInitialized)
        {
            return Result.Ok();
        }

        // Acquire a WebView from the factory
        _webView = await _webViewFactory.AcquireAsync();

        this.Content(_webView);

        // Set up virtual host mapping for Monaco editor assets
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "monaco.celbridge",
            "Celbridge.Code/Web/Monaco",
            CoreWebView2HostResourceAccessKind.Allow);

        // Ensure we only register the event handlers once
        _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        _webView.GotFocus -= WebView_GotFocus;

        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.GotFocus += WebView_GotFocus;

        // Initialize CodeEditorHost for RPC communication
        _messageChannel = new WebViewHostChannel(_webView.CoreWebView2);
        var celbridgeHost = new CelbridgeHost(_messageChannel);
        _host = new CodeEditorHost(celbridgeHost);

        // Register this control as the handler for RPC interfaces
        _host.AddLocalRpcTarget<IHostDocument>(this);
        _host.AddLocalRpcTarget<IHostInput>(this);

        _host.StartListening();

        // Sync WebView2 color scheme with the app theme
        ApplyThemeToWebView();

        // Prepare to wait for client ready notification
        _clientReadyTcs = new TaskCompletionSource();

        // Navigate to Monaco editor
        _webView.CoreWebView2.Navigate("http://monaco.celbridge/index.html");

        // Wait for the JS client to signal it's ready, with timeout to prevent infinite hang
        var timeout = TimeSpan.FromSeconds(ClientInitializationTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_clientReadyTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _clientReadyTcs = null;

            var errorMessage = $"Monaco editor client failed to initialize within {ClientInitializationTimeoutSeconds} seconds. " +
                               "The JavaScript module may have failed to load during pre-initialization.";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        _clientReadyTcs = null;
        _isPreInitialized = true;

        return Result.Ok();
    }

    /// <summary>
    /// Initializes the Monaco editor with content and language.
    /// If PreInitializeAsync() was called, this only sets the content and language.
    /// Otherwise, performs full initialization.
    /// </summary>
    public async Task<Result> InitializeAsync(string content, string language, string filePath, string resourceKey)
    {
        _content = content;
        _language = language;
        _filePath = filePath;
        _resourceKey = resourceKey;

        // If not pre-initialized, do the full initialization now
        if (!_isPreInitialized)
        {
            var preInitResult = await PreInitializeAsync();
            if (preInitResult.IsFailure)
            {
                return preInitResult;
            }
        }

        if (_host is null)
        {
            return Result.Fail("Failed to initialize JSON-RPC host");
        }

        // Initialize the Monaco editor via JSON-RPC with the content, language, and options
        await _host.InitializeEditorAsync(_language, _options.ScrollBeyondLastLine);

        return Result.Ok();
    }

    /// <summary>
    /// Gets the current content from the Monaco editor.
    /// This requests the content via RPC and waits for the response.
    /// Throws an exception if the Monaco editor fails to respond within the timeout period.
    /// </summary>
    public async Task<string> GetContentAsync()
    {
        if (_host is null)
        {
            return _content;
        }

        // Set up completion source to receive the content
        _getContentTcs = new TaskCompletionSource<string>();

        // Request content from Monaco - it will call SaveAsync with the content
        await _host.NotifyRequestSaveAsync();

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
    /// Sets the language mode of the Monaco editor.
    /// </summary>
    public async Task SetLanguageAsync(string language)
    {
        _language = language;

        if (_host is not null)
        {
            await _host.SetLanguageAsync(language);
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
    public async Task<Result> NavigateToLocationAsync(int lineNumber, int column, int endLineNumber = 0, int endColumn = 0)
    {
        if (_host is null)
        {
            return Result.Fail("Host is not initialized");
        }

        try
        {
            await _host.NavigateToLocationAsync(lineNumber, column, endLineNumber, endColumn);
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
        _host?.NotifyExternalChangeAsync();
    }

    /// <summary>
    /// Scrolls the Monaco editor to a specific percentage position.
    /// </summary>
    public async Task ScrollToPercentageAsync(double percentage)
    {
        if (_host is not null)
        {
            await _host.ScrollToPercentageAsync(percentage);
        }
    }

    /// <summary>
    /// Inserts text at the current cursor position (or replaces the current selection).
    /// </summary>
    public async Task InsertTextAtCaretAsync(string text)
    {
        if (_host is not null)
        {
            await _host.InsertTextAsync(text);
            _webView?.Focus(FocusState.Programmatic);
        }
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
        _host?.Dispose();
        _messageChannel?.Detach();
        _host = null;
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
            throw new LocalRpcException($"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
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
        // Use the content loader callback to get fresh content from the parent
        if (ContentLoader is not null)
        {
            _content = await ContentLoader();
        }
        else
        {
            _logger.LogWarning($"LoadAsync has no ContentLoader for file: {_resourceKey}");
        }

        var metadata = CreateMetadata();
        var result = new LoadResult(_content, metadata);

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

    public void OnDocumentChanged()
    {
        // Notify parent that content has changed
        ContentChanged?.Invoke();
    }

    public void OnClientReady()
    {
        // Signal that the JS client is ready
        _clientReadyTcs?.TrySetResult();
    }

    #endregion

    #region IHostInput

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    public void OnScrollPositionChanged(double scrollPercentage)
    {
        ScrollPositionChanged?.Invoke(scrollPercentage);
    }

    #endregion
}
