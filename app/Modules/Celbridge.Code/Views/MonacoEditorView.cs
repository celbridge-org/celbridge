using System.Text.Json;
using Celbridge.Code.MonacoHost;
using Celbridge.Code.ViewModels;
using Celbridge.Documents;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Code.Views;

public sealed partial class MonacoEditorView : DocumentView, IHostDocument, IHostNotifications
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    public MonacoEditorViewModel ViewModel { get; }

    private WebView2? _webView;
    private JsonRpc? _rpc;
    private HostRpcHandler? _rpcHandler;
    private HostChannel? _messageChannel;
    private TaskCompletionSource? _clientReadyTcs;

    public MonacoEditorView()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        ViewModel = ServiceLocator.AcquireService<MonacoEditorViewModel>();

        // Monitor theme changes to update Monaco editor theme
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        // Subscribe to reload requests from the ViewModel
        ViewModel.ReloadRequested += ViewModel_ReloadRequested;

        // Set the data context
        // The webview is not created until LoadContent is called, so we can pool webviews

        this.DataContext(ViewModel);
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

        if (_webView is not null)
        {
            // If _webView has already been created, then this method is being called as part of a resource rename/move.
            // Update the text editor language in case the file extension has changed.
            await UpdateTextEditorLanguage();
        }

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        _webView = await _webViewFactory.AcquireAsync();

        this.Content(_webView);

        // Load the document content
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}")
                .WithErrors(loadResult);
        }

        // Store the loaded text for the initialize handler
        ViewModel.CachedText = loadResult.Value;

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
        _webView.CoreWebView2.NewWindowRequested -= TextDocumentView_NewWindowRequested;
        _webView.GotFocus -= WebView_GotFocus;

        _webView.CoreWebView2.NewWindowRequested += TextDocumentView_NewWindowRequested;
        _webView.GotFocus += WebView_GotFocus;

        // Initialize StreamJsonRpc with this view as the handler
        _messageChannel = new HostChannel(_webView.CoreWebView2);
        _rpcHandler = new HostRpcHandler(_messageChannel);
        _rpc = new JsonRpc(_rpcHandler);

        // Ensure RPC method handlers run on the UI thread
        _rpc.SynchronizationContext = SynchronizationContext.Current;

        // Register this view as the handler for RPC interfaces
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
        var language = ViewModel.GetDocumentLanguage();
        await _rpc.NotifyEditorInitializeAsync(language);

        return Result.Ok();
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        if (_rpc is null)
        {
            return Result.Fail("RPC not initialized");
        }

        // Request the JS side to save - it will call document/save
        // which triggers our SaveAsync handler
        await _rpc.NotifyRequestSaveAsync();

        return await ViewModel.SaveDocument(ViewModel.CachedText ?? string.Empty);
    }

    public override async Task<Result> NavigateToLocation(string location)
    {
        if (_rpc is null)
        {
            return Result.Fail("RPC is not initialized");
        }

        if (string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        try
        {
            // Parse the location JSON to extract line number and column
            using var doc = JsonDocument.Parse(location);
            var root = doc.RootElement;

            var lineNumber = root.TryGetProperty("lineNumber", out var lineProp) ? lineProp.GetInt32() : 1;
            var column = root.TryGetProperty("column", out var colProp) ? colProp.GetInt32() : 1;

            // Navigate via JSON-RPC
            await _rpc.NotifyEditorNavigateToLocationAsync(lineNumber, column);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: {location}")
                .WithException(ex);
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        if (_webView == null)
        {
            return;
        }

        _webView.GotFocus -= WebView_GotFocus;

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NewWindowRequested -= TextDocumentView_NewWindowRequested;
        }

        // Unsubscribe from ViewModel events
        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        // Cleanup ViewModel message handlers
        ViewModel.Cleanup();

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
    }

    private void TextDocumentView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Prevent the new window from being created
        args.Handled = true;

        // Open the url in the default system browser
        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.NavigateToURL(url);
        }
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        // Set this document as the active document when the WebView2 receives focus
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private async Task UpdateTextEditorLanguage()
    {
        if (_rpc is null)
        {
            return;
        }

        var language = ViewModel.GetDocumentLanguage();
        await _rpc.NotifyEditorSetLanguageAsync(language);
    }

    private void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Notify JS to reload the document from disk
        _rpc?.NotifyExternalChangeAsync();
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
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));
    }

    #region IHostDocument

    public Task<InitializeResult> InitializeAsync(string protocolVersion)
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

        // Return the cached content
        var content = ViewModel.CachedText ?? string.Empty;

        return Task.FromResult(new InitializeResult(content, metadata, localization));
    }

    public async Task<LoadResult> LoadAsync()
    {
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            throw new HostRpcException(
                JsonRpcErrorCodes.InternalError,
                $"Failed to load document: {loadResult.Error}");
        }

        var content = loadResult.Value;
        ViewModel.CachedText = content;

        var metadata = CreateMetadata();

        return new LoadResult(content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            ViewModel.CachedText = content;
            var saveResult = await ViewModel.SaveDocument(content);

            if (saveResult.IsFailure)
            {
                return new SaveResult(false, saveResult.Error);
            }

            return new SaveResult(true);
        }
        catch (Exception ex)
        {
            return new SaveResult(false, ex.Message);
        }
    }

    #endregion

    #region IHostNotifications

    public void OnDocumentChanged()
    {
        // Mark the document as pending a save
        ViewModel.OnTextChanged();
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
