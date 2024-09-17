using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class TextEditorDocumentView : DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;

    public TextEditorDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    public TextEditorDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
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

        // Todo: Update the language of the document based on the file extension

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
        var text = loadResult.Value;

        // Send the loaded text content to Monaco editor
        _webView.CoreWebView2.PostWebMessageAsString(text);

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
        // TextEditorWebViewPool is not exposed via the public interface
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
        var readResult = await ReadTextData();
        if (readResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to save document: '{ViewModel.FileResource}'");
            failure.MergeErrors(readResult);
            return failure;
        }
        var textData = readResult.Value;

        return await ViewModel.SaveDocument(textData);
    }

    public override void PrepareToClose()
    {
        _webView!.WebMessageReceived -= TextDocumentView_WebMessageReceived;

        ReleaseTextEditorWebView();

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
            var textData = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(editorContent);

            if (textData != null)
            {
                return Result<string>.Ok(textData);
            }
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(ex, "An exception occured while reading the text data");
        }

        return Result<string>.Fail("Failed to read text data");
    }
}
