using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class TextDocumentView : DocumentView
{
    public TextDocumentViewModel ViewModel { get; }

    private readonly WebView2 _webView;

    private bool _isEditorReady;

    public TextDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<TextDocumentViewModel>();

        // Todo: Pool webviews for better responsiveness
        _webView = new WebView2();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;
        _webView.Visibility = Visibility.Collapsed;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));

        // Note that we can't use the Loaded and Unloaded events here because those events are fired when
        // the tab views are reordered by the user.
        _webView.WebMessageReceived += TextDocumentView_WebMessageReceived;
    }

    public override void SetFileResourceAndPath(ResourceKey fileResource, string filePath)
    {
        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;
    }

    public override async Task<Result> LoadContent()
    {
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
        _webView.WebMessageReceived -= TextDocumentView_WebMessageReceived;
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

            // Todo: log error
            // Log.Error($"Expected 'editor_ready' message, but received: {e.TryGetWebMessageAsString()}");
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
