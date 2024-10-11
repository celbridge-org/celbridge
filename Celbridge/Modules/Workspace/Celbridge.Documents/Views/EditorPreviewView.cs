using Celbridge.Documents.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class EditorPreviewView : UserControl
{
    public EditorPreviewViewModel ViewModel { get; }

    private WebView2 _webView;

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
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    private async Task InitializeWebView()
    {
        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "Preview",
            "preview",
            CoreWebView2HostResourceAccessKind.Allow);

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

        _webView.CoreWebView2.Navigate("http://Preview/index.html");
    }
}
