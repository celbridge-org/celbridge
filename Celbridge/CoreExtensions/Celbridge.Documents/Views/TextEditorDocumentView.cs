using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class TextEditorDocumentView : DocumentView
{
    private readonly ILogger<TextEditorDocumentView> _logger;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;

    public TextEditorDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

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
        var language = ViewModel.GetDocumentLanguage();
        _webView = await AcquireTextEditorWebView(language);
        this.Content(_webView);

        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}");
            failure.MergeErrors(loadResult);
            return failure;
        }

        // Send the loaded text content to Monaco editor
        _webView.CoreWebView2.PostWebMessageAsString(ViewModel.Text);

        // Listen for text updates from the web view
        _webView.WebMessageReceived += TextDocumentView_WebMessageReceived;

        return Result.Ok();
    }

    private async Task<WebView2> AcquireTextEditorWebView(string language)
    {
        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var pool = documentsService.TextEditorWebViewPool;

        var webView = await pool.AcquireTextEditorWebView(language);

        return webView;
    }

    private void ReleaseTextEditorWebView()
    {
        var documentsService = _documentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        var pool = documentsService.TextEditorWebViewPool;

        pool.ReleaseTextEditorWebView(_webView!);
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

        ReleaseTextEditorWebView();

        _webView = null;
    }

    private void TextDocumentView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Update the text in the view model
        // Todo: Use a web message to flag when the content has changed, then request it when saving.
        // Hopefully we can just call a js function to do that rather than using messages.

        // Todo: Set the dirty flag and call this when it's time to save
        var message = e.TryGetWebMessageAsString();
        if (message == "did_change_content")
        {
             _ = ReadTextData();
        }
    }

    private async Task ReadTextData()
    {
        if (_webView == null)
        {
            return;
        }

        var script = "getTextData();";
        var editorContent = await _webView.ExecuteScriptAsync(script);
        var textData = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(editorContent);

        if (textData != null)
        {
            ViewModel.Text = textData;
        }
    }
}
