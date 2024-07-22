using Celbridge.Settings;
using Microsoft.Web.WebView2.Core;
using Path = System.IO.Path;

namespace Celbridge.Legacy.Views;

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

                EditorWebView.Visibility = Visibility.Visible;

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
            try
            {
                async Task SetupCodeEditor()
                {
                    // The content to edit has been loaded from disk now, so it's safe to 
                    // initialize the Monaco editor.

                    await EditorWebView.EnsureCoreWebView2Async();
                    EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "MonacoEditor",
                        "monaco",
                        CoreWebView2HostResourceAccessKind.Allow);

                    await EditorWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

                    string language = GetLanguage(ViewModel.Path);
                    await EditorWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.language = '{language}';");

                    var settingsService = LegacyServiceProvider.Services!.GetRequiredService<ISettingsService>();
                    Guard.IsNotNull(settingsService);
                    Guard.IsNotNull(settingsService.EditorSettings);

                    //var themeSetting = settingsService.EditorSettings.Theme;
                    //var theme = themeSetting == "Dark" ? "vs-dark" : "vs-light";
                    var theme = "vs-dark";

                    await EditorWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{theme}';");

                    EditorWebView.CoreWebView2.Navigate("http://MonacoEditor/index.html");

                    // WebView navigation will complete in a while, and then the Monaco editor will also take some time to initialize.
                    // The web app sends a "editor_ready" message back to Celbridge once the editor is ready to accept content.
                }

                _ = SetupCodeEditor();
            }
            catch (Exception ex)
            {
                return new ErrorResult(ex.Message);
            }
        }

        return result;
    }

    private void SynchronizeContent()
    {
        Guard.IsTrue(_isEditorReady, "Failed to synchronize content because editor is not ready.");

        EditorWebView.CoreWebView2.PostWebMessageAsString(ViewModel.Content);
    }

    private string GetLanguage(string filename)
    {
        string language;
        var extension = Path.GetExtension(filename).ToLowerInvariant();

        switch (extension)
        {
            case ".js":
                language = "javascript";
                break;
            case ".ts":
                language = "typescript";
                break;
            case ".json":
                language = "json";
                break;
            case ".html":
            case ".htm":
                language = "html";
                break;
            case ".css":
                language = "css";
                break;
            case ".scss":
                language = "scss";
                break;
            case ".less":
                language = "less";
                break;
            case ".md":
                language = "markdown";
                break;
            case ".py":
                language = "python";
                break;
            case ".java":
                language = "java";
                break;
            case ".c":
                language = "c";
                break;
            case ".cpp":
                language = "cpp";
                break;
            case ".cs":
                language = "csharp";
                break;
            case ".php":
                language = "php";
                break;
            case ".rb":
                language = "ruby";
                break;
            case ".go":
                language = "go";
                break;
            case ".lua":
                language = "lua";
                break;
            case ".xml":
                language = "xml";
                break;
            case ".sql":
                language = "sql";
                break;
            case ".yaml":
            case ".yml":
                language = "yaml";
                break;
            case ".sh":
                language = "shell";
                break;
            // Add more cases as needed
            default:
                language = "plaintext";
                break;
        }

        return language;
    }
}
