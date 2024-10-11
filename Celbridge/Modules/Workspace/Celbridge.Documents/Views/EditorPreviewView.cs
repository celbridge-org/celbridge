using Celbridge.Documents.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class EditorPreviewView : UserControl
{
    public EditorPreviewViewModel ViewModel { get; }

    private WebView2 _webView;

    private bool _loaded;

    public EditorPreviewView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<EditorPreviewViewModel>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _webView = new WebView2();
            
        this.DataContext(ViewModel)
            .Content(_webView);

        _ = InitializeWebView();
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.PreviewHTML))
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

            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    private async Task InitializeWebView()
    {
        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        // Add a mapping for the "preview" files packaged with the build.
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "Preview",
            "preview",
            CoreWebView2HostResourceAccessKind.Allow);

        // Add a mapping for the project folder so relative links work.
        var projectFolder = ViewModel.ProjectFolderPath;
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "Project",
            projectFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

        _webView.NavigationCompleted += (s, e) =>
        {
            _loaded = true;

            // Any further navigation is caused by the user clicking on links in the preview pane.
            _webView.NavigationStarting += WebView_NavigationStarting;
        };

        _webView.CoreWebView2.Navigate("http://Preview/index.html");
    }

    private void WebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Prevent the WebView from navigating to the URL
        args.Cancel = true;

        // Open the url in the default system browser
        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.NavigateToURL(url);
        }
    }
}
