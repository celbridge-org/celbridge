using Celbridge.Documents.ViewModels;
using Microsoft.Web.WebView2.Core;

using Path = System.IO.Path;

namespace Celbridge.Documents.Views;

public sealed partial class EditorPreviewView : UserControl, IEditorPreview
{
    public EditorPreviewViewModel ViewModel { get; }

    private WebView2? _webView;

    private bool _loaded = false;

    public EditorPreviewView()
    {
        ViewModel = ServiceLocator.AcquireService<EditorPreviewViewModel>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        this.DataContext(ViewModel);
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.FilePath))
        {
            await InitializeWebView(ViewModel.FilePath);
        }
        else if (e.PropertyName == nameof(ViewModel.PreviewHTML))
        {
            // Escape special characters in the HTML content
            // Todo: If this isn't sufficient, we might need to serialize the HTML content to JSON.
            var html = ViewModel.PreviewHTML
                .Replace("\\", "\\\\")  // Escape backslashes
                .Replace("`", "\\`")    // Escape backticks for template literals
                .Replace("\n", "\\n")   // Escape newlines
                .Replace("\r", "\\r");  // Escape carriage returns

            var script = $"setContent(`{html}`);";

            // The page may still be navigating, so wait until it's loaded before executing the script
            while (!_loaded)
            {
                await Task.Delay(100);
            }

            Guard.IsNotNull(_webView);
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    private async Task InitializeWebView(string filePath)
    {
        // This method can be called multiple times, e.g. when a file is renamed.

        _loaded = false;
        _webView = new WebView2();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        await _webView.EnsureCoreWebView2Async();

        // Add a mapping for the "preview" files packaged with the build.
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "Preview",
            "preview",
            CoreWebView2HostResourceAccessKind.Allow);

        // Add a mapping for the file's parent folder so that relative links work.
        var folder = Path.GetDirectoryName(ViewModel.FilePath);
        Guard.IsNotNull(folder);

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "Project",
            folder,
            CoreWebView2HostResourceAccessKind.Allow);

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

        _webView.NavigationCompleted += WebView_NavigationCompleted;

        _webView.CoreWebView2.Navigate("http://Preview/index.html");
    }

    private void WebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        Guard.IsNotNull(_webView);

        _loaded = true;
        _webView.NavigationCompleted -= WebView_NavigationCompleted;

        // Any further navigation is caused by the user clicking on links in the preview pane.
        _webView.NavigationStarting += WebView_NavigationStarting;
        _webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

        // Display the webview
        Content = _webView;
    }

    private async void WebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        Guard.IsNotNull(_webView);

        // Prevent the WebView from navigating to the URL
        args.Cancel = true;

        // Open the url in the default system browser
        var url = args.Uri;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (url.StartsWith("https://project/"))
        {
            var relativePath = url.Substring("https://project/".Length);

            if (relativePath.StartsWith('#'))
            {
                var script = $"window.location.hash = '{relativePath}';";
                await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }

            // Todo: Log error if this fails
            ViewModel.OpenRelativePath(relativePath);
        }
        else
        {
            ViewModel.NavigateToURL(url);
        }
    }

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Prevent the new window from being created
        args.Handled = true;

        // Open the url in the default system browser
        var url = args.Uri;

        //if (url.StartsWith("http://project/"))

        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.NavigateToURL(url);
        }
    }

    public async Task<Result<string>> ConvertAsciiDocToHTML(string asciiDoc)
    {
        while (!_loaded)
        {
            await Task.Delay(50);
        }

        Guard.IsNotNull(_webView);

        // Escape special characters in the asciiDoc content
        // Todo: If this isn't sufficient, we might need to serialize the HTML content to JSON.
        var escaped = asciiDoc
            .Replace("\\", "\\\\")  // Escape backslashes
            .Replace("`", "\\`")    // Escape backticks for template literals
            .Replace("\n", "\\n")   // Escape newlines
            .Replace("\r", "\\r");  // Escape carriage returns

        var script = $"convertAsciiDoc(`{escaped}`);";

        var html = await _webView.ExecuteScriptAsync(script);

        string unescapedHtml = System.Text.RegularExpressions.Regex.Unescape(html);
        if (unescapedHtml.Length >= 2)
        {
            // Remove the surrounding quotes
            unescapedHtml = unescapedHtml[1..^1];
        }

        // Todo: Handle errors

        return Result<string>.Ok(unescapedHtml);
    }
}
