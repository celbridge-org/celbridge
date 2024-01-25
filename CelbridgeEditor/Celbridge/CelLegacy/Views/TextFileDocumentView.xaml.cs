using Microsoft.Web.WebView2.Core;

namespace CelLegacy.Views;

public partial class TextFileDocumentView : TabViewItem, IDocumentView
{
    private bool _isEditorReady;

    public TextFileDocumentViewModel ViewModel { get; }

    public TextFileDocumentView()
    {
        this.InitializeComponent();

        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<TextFileDocumentViewModel>();

        Loaded += (s, e) =>
        {
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;
        };

        Unloaded += (s, e) =>
        {
            ViewModel.LoadedContent -= ViewModel_LoadedContent;
        };
    }

    private void EditorWebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_isEditorReady)
        {
            if (e.TryGetWebMessageAsString() == "editor_ready")
            {
                _isEditorReady = true;
                SynchronizeContent();

                ViewModel.LoadedContent += ViewModel_LoadedContent;
                return;
            }

            Log.Error($"Expected 'editor_ready' message, but received: {e.TryGetWebMessageAsString()}");
            return;
        }

        ViewModel.Content = e.TryGetWebMessageAsString();
    }

    private void ViewModel_LoadedContent()
    {
        SynchronizeContent();
    }

    public IDocument Document
    { 
        get => ViewModel.Document;
        set => ViewModel.Document = value;
    }

    public void CloseDocument()
    {
        ViewModel.CloseDocumentCommand.ExecuteAsync(null);

        // Shutdown the web view to release resources immediately
        EditorWebView.WebMessageReceived -= EditorWebView_WebMessageReceived;
        EditorWebView.Close();
    }

    public async Task<Result> LoadDocumentAsync()
    {
        Guard.IsFalse(_isEditorReady);

        var result = await ViewModel.LoadDocumentAsync();
        if (result.Success)
        {
            async Task SetupCodeEditor()
            {
                // The content to edit has been loaded from disk now, so it's safe to 
                // initialize the Monaco editor.

                await EditorWebView.EnsureCoreWebView2Async();
                EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "CelbridgeBlazor",
                    "wwwroot",
                    CoreWebView2HostResourceAccessKind.Allow);
                await EditorWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");
                EditorWebView.CoreWebView2.Navigate("http://CelbridgeBlazor/index.html?redirect=editor");

                // WebView navigation will complete in a while, and then the Monaco editor will take some time to initialize.
                // The web app sends a "editor_ready" message back to Celbridge once the editor is ready to accept content.
            }

            _ = SetupCodeEditor();
        }

        return result;
    }

    private void SynchronizeContent()
    {
        Guard.IsTrue(_isEditorReady, "Failed to synchronize content because editor is not ready.");

        EditorWebView.CoreWebView2.PostWebMessageAsString(ViewModel.Content);
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Log.Information($"Navigation starting: {args.Uri}");
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        // Log.Information($"Navigation completed: {EditorWebView.Source}");
    }
}
