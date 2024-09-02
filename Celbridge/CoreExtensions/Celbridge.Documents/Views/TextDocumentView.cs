using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using CommunityToolkit.Diagnostics;
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

        _webView = new WebView2();

        // Fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));

        // Todo: These callbacks are probably not safe to use!

        Loaded += (s, e) =>
        {
            _webView.WebMessageReceived += TextDocumentView_WebMessageReceived;
        };

        Unloaded += (s, e) =>
        {
            // ViewModel.LoadedContent -= ViewModel_LoadedContent;
        };
    }

    public override void UpdateDocumentResource(ResourceKey fileResource, string filePath)
    {
        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;
    }

    public async Task IntializeEditor()
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

    private void TextDocumentView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_isEditorReady)
        {
            if (e.TryGetWebMessageAsString() == "editor_ready")
            {
                _isEditorReady = true;
                SynchronizeContent();

                _webView.Visibility = Visibility.Visible;

                ViewModel.LoadedContent += ViewModel_LoadedContent;
                return;
            }

            // Todo: log error
            // Log.Error($"Expected 'editor_ready' message, but received: {e.TryGetWebMessageAsString()}");
            return;
        }

        ViewModel.Text = e.TryGetWebMessageAsString();
    }

    private void ViewModel_LoadedContent()
    {
        SynchronizeContent();
    }

    private void SynchronizeContent()
    {
        Guard.IsTrue(_isEditorReady, "Failed to synchronize content because editor is not ready.");

        _webView.CoreWebView2.PostWebMessageAsString(ViewModel.Text);
    }
}
