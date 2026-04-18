using Celbridge.Code.Services;
using Celbridge.Commands;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// A reusable control that hosts a code editor via WebView2.
/// This is a pure text editing control that can be embedded in any document view.
/// The parent view is responsible for file I/O and document management.
/// </summary>
public sealed partial class CodeEditor : UserControl
{
    private const int ContentRequestTimeoutSeconds = 5;
    private const int ClientInitializationTimeoutSeconds = 10;
    private const int ContentLoadedTimeoutSeconds = 10;

    private readonly ILogger<CodeEditor> _logger;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ICommandService _commandService;

    private readonly CodeEditorState _state = new();

    private WebView2? _webView;
    private CodeEditorHost? _host;
    private WebViewHostChannel? _messageChannel;
    private CodeEditorDocumentHandler? _documentHandler;

    private bool _isPreInitialized;
    private string _language = "plaintext";

    /// <summary>
    /// Callback to load content from the parent. Set this before calling InitializeAsync().
    /// The parent is responsible for reading from disk or other source.
    /// </summary>
    public Func<Task<string>>? ContentLoader
    {
        get => _state.ContentLoader;
        set => _state.ContentLoader = value;
    }

    /// <summary>
    /// Editor options. Set this before calling InitializeAsync() or PreInitializeAsync().
    /// </summary>
    public CodeEditorOptions Options { get; set; } = CodeEditorOptions.Default;

    /// <summary>
    /// Optional URL to a customization script for the Monaco editor.
    /// The script is loaded after Monaco initializes and should export an activate(monaco, editor, container, celbridge) function.
    /// Set this before calling InitializeAsync().
    /// </summary>
    public string? CustomizationScriptUrl { get; set; }

    /// <summary>
    /// Raised when the content changes in the Monaco editor (user editing).
    /// </summary>
    public event Action? ContentChanged;

    /// <summary>
    /// Raised when the JS client requests a content reload via the document/load RPC.
    /// This fires during external change reloads, before the JS sets the new content in the editor.
    /// </summary>
    public event Action? ContentLoadRequested;

    /// <summary>
    /// Raised every time the Monaco editor has finished loading or reloading content.
    /// The reason argument distinguishes the initial load from an external-change reload.
    /// </summary>
    public event Action<ContentLoadedReason>? ContentLoaded;

    /// <summary>
    /// Raised when the editor receives focus.
    /// </summary>
    public event Action? EditorFocused;

    /// <summary>
    /// Raised when the scroll position changes in the Monaco editor.
    /// The parameter is the scroll percentage (0.0 to 1.0).
    /// </summary>
    public event Action<double>? ScrollPositionChanged;

    /// <summary>
    /// Raised when the scroll position changes in the split-editor preview pane.
    /// The parameter is the scroll percentage (0.0 to 1.0).
    /// </summary>
    public event Action<double>? PreviewScrollPositionChanged;

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

        _documentHandler = new CodeEditorDocumentHandler(
            _logger,
            _state,
            () => ContentChanged?.Invoke());
        _documentHandler.ContentLoadRequested += () => ContentLoadRequested?.Invoke();
        _documentHandler.ContentLoaded += reason => ContentLoaded?.Invoke(reason);

        var inputHandler = new CodeEditorInputHandler(
            _state,
            scrollPercentage => ScrollPositionChanged?.Invoke(scrollPercentage),
            scrollPercentage => PreviewScrollPositionChanged?.Invoke(scrollPercentage));

        _host.AddLocalRpcTarget<IHostDocument>(_documentHandler);
        _host.AddLocalRpcTarget<IHostInput>(inputHandler);

        _host.StartListening();

        // Sync WebView2 color scheme with the app theme
        ApplyThemeToWebView();

        var clientReadyTcs = new TaskCompletionSource();
        _documentHandler.ClientReadyTcs = clientReadyTcs;

        // Navigate to Monaco editor
        _webView.CoreWebView2.Navigate("http://monaco.celbridge/index.html");

        // Wait for the JS client to signal it's ready, with timeout to prevent infinite hang
        var timeout = TimeSpan.FromSeconds(ClientInitializationTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(clientReadyTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _documentHandler.ClientReadyTcs = null;

            var errorMessage = $"Monaco editor client failed to initialize within {ClientInitializationTimeoutSeconds} seconds. " +
                               "The JavaScript module may have failed to load during pre-initialization.";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        _documentHandler.ClientReadyTcs = null;
        _isPreInitialized = true;

        return Result.Ok();
    }

    /// <summary>
    /// Initializes the Monaco editor with content and language.
    /// If PreInitializeAsync() was called, this only sets the content and language.
    /// Otherwise, performs full initialization.
    /// Waits for Monaco to signal that content is loaded before returning.
    /// </summary>
    public async Task<Result> InitializeAsync(string content, string language, string filePath, string resourceKey, string projectFolderPath = "")
    {
        _state.Content = content;
        _language = language;
        _state.FilePath = filePath;
        _state.ResourceKey = resourceKey;
        if (!string.IsNullOrEmpty(projectFolderPath))
        {
            _state.ProjectFolderPath = projectFolderPath;
        }

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

        // Map the project folder so preview renderers can resolve relative image/resource URLs.
        // Harmless when no preview renderer is attached because nothing references project.celbridge.
        // Called here (not in PreInitializeAsync) because the project path isn't known
        // during pre-warm; SetVirtualHostNameToFolderMapping is idempotent so calling it
        // again on subsequent initializations is safe.
        if (!string.IsNullOrEmpty(_state.ProjectFolderPath) && _webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "project.celbridge",
                _state.ProjectFolderPath,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        var contentLoadedTcs = new TaskCompletionSource();
        _documentHandler!.ContentLoadedTcs = contentLoadedTcs;

        // Initialize the Monaco editor via JSON-RPC with the content, language, and options
        await _host.InitializeEditorAsync(_language, Options);

        // Wait for Monaco to signal content is loaded, with timeout
        var timeout = TimeSpan.FromSeconds(ContentLoadedTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(contentLoadedTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _documentHandler.ContentLoadedTcs = null;

            var errorMessage = $"Monaco editor content failed to load within {ContentLoadedTimeoutSeconds} seconds. " +
                               $"File: {_state.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        _documentHandler.ContentLoadedTcs = null;

        // Apply customization script if configured
        if (!string.IsNullOrEmpty(CustomizationScriptUrl) && _host is not null)
        {
            await _host.ApplyCustomizationAsync(CustomizationScriptUrl);
        }

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
            return _state.Content;
        }

        var getContentTcs = new TaskCompletionSource<string>();
        _documentHandler!.GetContentTcs = getContentTcs;

        // Request content from Monaco - it will call SaveAsync with the content
        await _host.NotifyRequestSaveAsync();

        // Wait for SaveAsync to be called, with timeout to prevent hanging forever
        var timeout = TimeSpan.FromSeconds(ContentRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(getContentTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _documentHandler.GetContentTcs = null;

            var errorMessage = $"Monaco editor failed to respond within {ContentRequestTimeoutSeconds} seconds. " +
                               $"The editor may be in an unstable state. File: {_state.FilePath}";

            _logger.LogError(errorMessage);

            throw new TimeoutException(errorMessage);
        }

        var content = await getContentTcs.Task;
        _documentHandler.GetContentTcs = null;

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
        _state.FilePath = filePath;
        _state.ResourceKey = resourceKey;

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
    /// Attaches or detaches a split-editor preview renderer.
    /// Pass a URL to an ES module that implements the preview contract to enable the
    /// split editor; pass null to disable it.
    /// Safe to call before or after InitializeAsync; pre-init values update the options
    /// used by the first initialize RPC, post-init values drive the setPreviewRenderer RPC.
    /// </summary>
    public async Task SetPreviewRendererAsync(string? rendererUrl)
    {
        Options = Options with { PreviewRendererUrl = rendererUrl };

        if (_host is not null)
        {
            await _host.SetPreviewRendererAsync(rendererUrl);
        }
    }

    /// <summary>
    /// Sets the current view mode (source | split | preview).
    /// Only meaningful when a preview renderer is attached.
    /// </summary>
    public async Task SetViewModeAsync(string viewMode)
    {
        if (_host is not null)
        {
            await _host.SetViewModeAsync(viewMode);
        }
    }

    /// <summary>
    /// Updates the base path used by the preview renderer to resolve relative resource references.
    /// </summary>
    public async Task UpdateBasePathAsync(string basePath)
    {
        if (_host is not null)
        {
            await _host.SetBasePathAsync(basePath);
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
    /// Applies a batch of text edits to the Monaco editor as a single undo unit.
    /// Each edit specifies a range (line, column, endLine, endColumn) and replacement text.
    /// </summary>
    public async Task ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        if (_host is not null)
        {
            await _host.ApplyEditsAsync(edits);
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

}
