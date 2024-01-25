using Microsoft.Web.WebView2.Core;

namespace CelLegacy.Views;

public partial class TextFileDocumentView : TabViewItem, IDocumentView
{
    private bool _codeEditorLoaded;

    public TextFileDocumentViewModel ViewModel { get; }

    public TextFileDocumentView()
    {
        this.InitializeComponent();

        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<TextFileDocumentViewModel>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Don't change background color on hover or focus. No IDE does this.
        var brush = (SolidColorBrush)Application.Current.Resources["PanelBackgroundABrush"];
        ContentTextBox.Resources["TextControlBackgroundPointerOver"] = brush;
        ContentTextBox.Resources["TextControlBackgroundFocused"] = brush;

        Loaded += (s, e) =>
        {
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;
        };
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextFileDocumentViewModel.Content))
        {
            if (_codeEditorLoaded)
            {
                async Task CallJSFunction(string content)
                {
                    // Todo: Use PostWebMessageAsJson instead
                    //EditorWebView.CoreWebView2.PostWebMessageAsString(content);

                    var json = JsonConvert.SerializeObject(content);
                    await EditorWebView.ExecuteScriptAsync($"setTextData({json})");
                }

                _ = CallJSFunction(ViewModel.Content);
            }
        }
    }

    private void EditorWebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Todo: At this point the WebView and Monaco Editor are fully initialized.
        // There are several asynchronous processes going on here though, so we should only
        // actually populate the editor when the text is loaded from disk and the editor view is ready to receive the text.
        // It's also very slow loading the editor view, see if AOT compiling makes it more snappy.

        var payload = e.WebMessageAsJson;
        Log.Information($"Got payload: {payload}");
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
        var result = await ViewModel.LoadDocumentAsync();
        if (result.Success)
        {
            async Task LoadCodeEditor()
            {
                await EditorWebView.EnsureCoreWebView2Async();
                EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "CelbridgeBlazor",
                    "wwwroot",
                    CoreWebView2HostResourceAccessKind.Allow);
                await EditorWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");
                EditorWebView.CoreWebView2.Navigate("http://CelbridgeBlazor/index.html?redirect=editor");
            }

            _ = LoadCodeEditor();
        }

        return result;
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Log.Information($"Navigation starting: {args.Uri}");
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _codeEditorLoaded = true;
        // Log.Information($"Navigation completed: {EditorWebView.Source}");
    }
}
