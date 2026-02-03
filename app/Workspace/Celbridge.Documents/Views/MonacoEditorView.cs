using System.Text.Json;
using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class MonacoEditorView : DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;
    private readonly IMessengerService _messengerService;

    public MonacoEditorViewModel ViewModel { get; }

    private WebView2? _webView;

    public MonacoEditorView()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        ViewModel = ServiceLocator.AcquireService<MonacoEditorViewModel>();

        // Subscribe to reload requests from the ViewModel
        ViewModel.ReloadRequested += ViewModel_ReloadRequested;

        // Set the data context
        // The webview is not created until LoadContent is called, so we can pool webviews

        this.DataContext(ViewModel);
    }

    public Result SetContent(string content)
    {
        if (_webView is null ||
            _webView.CoreWebView2 is null)
        {
            return Result.Fail("WebView is not initialized");
        }

        // Send the updated text content to Monaco editor
        _webView.CoreWebView2.PostWebMessageAsString(content);
        ViewModel.CachedText = content;

        return Result.Ok();
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
        // TextEditorWebViewPool is not exposed via the public interface
        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var pool = documentsService.TextEditorWebViewPool;

        _webView = await pool.AcquireInstance();

        await UpdateTextEditorLanguage();

        this.Content(_webView);

        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}")
                .WithErrors(loadResult);
        }
        var text = loadResult.Value;

        // Send the loaded text content to Monaco editor
        _webView.CoreWebView2.PostWebMessageAsString(text);

        // Ensure we only register the event handlers once
        _webView.WebMessageReceived -= TextDocumentView_WebMessageReceived;
        _webView.CoreWebView2.NewWindowRequested -= TextDocumentView_NewWindowRequested;
        _webView.GotFocus -= WebView_GotFocus;

        // Start listening for text updates from the web view
        _webView.WebMessageReceived += TextDocumentView_WebMessageReceived;
        _webView.CoreWebView2.NewWindowRequested += TextDocumentView_NewWindowRequested;
        _webView.GotFocus += WebView_GotFocus;

        return Result.Ok();
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        var readResult = await ReadTextData();
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to save document: '{ViewModel.FileResource}'")
                .WithErrors(readResult);
        }
        var textData = readResult.Value;

        return await ViewModel.SaveDocument(textData);
    }

    public override async Task<Result> NavigateToLocation(string location)
    {
        if (_webView == null ||
            _webView.CoreWebView2 == null)
        {
            return Result.Fail("WebView is not initialized");
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

            // Call the JavaScript function to navigate to the location
            var script = $"navigateToLocation({lineNumber}, {column});";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);

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
        if (_webView == null)
        {
            return;
        }

        _webView.WebMessageReceived -= TextDocumentView_WebMessageReceived;
        _webView.GotFocus -= WebView_GotFocus;

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NewWindowRequested -= TextDocumentView_NewWindowRequested;
        }

        // Unsubscribe from ViewModel events
        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        // Cleanup ViewModel message handlers
        ViewModel.Cleanup();

        // Release the webview back to the pool.
        // TextEditorWebViewPool is not exposed via the public interface
        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var pool = documentsService.TextEditorWebViewPool;

        await pool.ReleaseInstanceAsync(_webView);

        _webView = null;
    }

    private void TextDocumentView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (message == "did_change_content")
        {
            // Mark the document as pending a save
            ViewModel.OnTextChanged();
        }
        else if (message == "toggle_layout")
        {
            ViewModel.ToggleLayout();
        }
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
        Guard.IsNotNull(_webView);

        var language = ViewModel.GetDocumentLanguage();

        var script = $"setLanguage('{language}');";
        await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task<Result<string>> ReadTextData()
    {
        if (_webView == null)
        {
            return Result<string>.Fail("WebView is null");
        }

        try
        {
            var script = "getTextData();";
            var editorContent = await _webView.ExecuteScriptAsync(script);
            var textData = JsonSerializer.Deserialize<string>(editorContent);

            if (textData != null)
            {
                return Result<string>.Ok(textData);
            }
        }
        catch (Exception ex)
        {
            return Result<string>.Fail("An exception occured while reading the text data")
                .WithException(ex);
        }

        return Result<string>.Fail("Failed to read text data");
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // Reload the document from disk when an external change is detected
        if (_webView != null)
        {
            var loadResult = await ViewModel.LoadDocument();
            if (loadResult.IsSuccess)
            {
                var text = loadResult.Value;
                _webView.CoreWebView2.PostWebMessageAsString(text);
            }
        }
    }
}
