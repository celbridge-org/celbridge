using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class TextEditorDocumentView : DocumentView
{
    private readonly ILogger<TextEditorDocumentView> _logger;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;

    public TextEditorDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    private bool _isEditorReady;

    public TextEditorDocumentView(
        ILogger<TextEditorDocumentView> logger,
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        ViewModel = serviceProvider.GetRequiredService<TextEditorDocumentViewModel>();

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel);

    }

    public override Result SetFileResource(ResourceKey fileResource)
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

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        _webView = await AcquireWebView();
        this.Content(_webView);

        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}");
            failure.MergeErrors(loadResult);
            return failure;
        }

        var initResult = await InitializeTextEditor();
        if (initResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}");
            failure.MergeErrors(initResult);
            return failure;
        }

        // Wait until Monaco editor has initialized
        while (!_isEditorReady)
        {
            await Task.Delay(50);
        }

        // Send the loaded text to Monaco editor
        _webView.CoreWebView2.PostWebMessageAsString(ViewModel.Text);

        return Result.Ok();
    }

    private async Task<WebView2> AcquireWebView()
    {
        // Pool webviews to improve responsiveness

        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var textEditorPool = documentsService.TextEditorPool;

        var webView = await textEditorPool.AcquireTextEditorWebView();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        webView.DefaultBackgroundColor = Colors.Transparent;
        //webView.Visibility = Visibility.Collapsed;

        // Note that we can't use the Loaded and Unloaded events here because those events are fired when
        // the tab views are reordered by the user.
        webView.WebMessageReceived += TextDocumentView_WebMessageReceived;

        return webView;
    }

    private void ReleaseWebView()
    {
        // Pool webviews to improve responsiveness

        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var textEditorPool = documentsService.TextEditorPool;

        textEditorPool.ReleaseTextEditorWebView(_webView!);
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        return await ViewModel.SaveDocument();
    }

    public override void OnDocumentClosing()
    {
        _webView!.WebMessageReceived -= TextDocumentView_WebMessageReceived;

        ReleaseWebView();

        _webView = null;
    }

    private void TextDocumentView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_isEditorReady)
        {
            if (e.TryGetWebMessageAsString() == "editor_ready")
            {
                _isEditorReady = true;
                _webView.Visibility = Visibility.Visible;
                return;
            }

            _logger.LogError($"Expected 'editor_ready' message, but received: {e.TryGetWebMessageAsString()}");
            return;
        }

        // Update the text in the view model
        // Todo: Use a web message to flag when the content has changed, then request it when saving
        // Hopefully we can just call a js function to do that rather than using messages

        ViewModel.Text = e.TryGetWebMessageAsString();
    }

    private async Task<Result> InitializeTextEditor()
    {
        try
        {
            // The content to edit has been loaded from disk now, so it's safe to 
            // initialize the Monaco editor.

            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "MonacoEditor",
                "monaco",
                CoreWebView2HostResourceAccessKind.Allow);

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            string language = ViewModel.GetLanguage();
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.language = '{language}';");

            //var settingsService = LegacyServiceProvider.Services!.GetRequiredService<ISettingsService>();
            //Guard.IsNotNull(settingsService);
            //Guard.IsNotNull(settingsService.EditorSettings);
            //var themeSetting = settingsService.EditorSettings.Theme;
            //var theme = themeSetting == "Dark" ? "vs-dark" : "vs-light";

            var theme = "vs-dark";
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{theme}';");

            _webView.CoreWebView2.Navigate("http://MonacoEditor/index.html");

            // WebView navigation will complete in a while, and then the Monaco editor will also take some time to initialize.
            // The web app sends a "editor_ready" message back to Celbridge once the editor is ready to accept content.
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to initialize Monaco editor");
        }

        return Result.Ok();
    }
}
